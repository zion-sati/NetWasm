import { resourceDisposeSymbol } from "./resource-disposal.mjs";

const maximumSize = (1n << 64n) - 1n;
const pathFlagNames = ["symlinkFollow"];
const openFlagNames = ["create", "directory", "exclusive", "truncate"];
const descriptorFlagNames = [
  "read", "write", "fileIntegritySync", "dataIntegritySync", "requestedWriteSync", "mutateDirectory",
];

export function createReadOnlyFileDirectory(options) {
  if (options === null || typeof options !== "object" || Array.isArray(options)) {
    throw new TypeError("read-only file directory options are required");
  }
  const { fileName, bytes } = options;
  if (typeof fileName !== "string" || fileName.length === 0
      || fileName === "." || fileName === ".." || /[/\\\0]/u.test(fileName)) {
    throw new TypeError("read-only file name must be one non-empty path segment");
  }
  if (!(bytes instanceof Uint8Array)) {
    throw new TypeError("read-only file contents must be bytes");
  }
  let contents = new Uint8Array(bytes);
  const directoryDescriptors = new WeakSet();
  const fileDescriptors = new WeakSet();

  function createDescriptor(kind, canRead) {
    const identities = kind === "directory" ? directoryDescriptors : fileDescriptors;
    let disposed = false;
    function assertAlive() {
      if (disposed || contents === null) throw "bad-descriptor";
    }
    function denyMutation() {
      assertAlive();
      throw "read-only";
    }
    function unsupported() {
      assertAlive();
      throw "unsupported";
    }
    const descriptor = Object.freeze({
      openAt(pathFlags, path, openFlags, flags) {
        assertAlive();
        if (kind !== "directory") throw "not-directory";
        assertFlags(pathFlags, pathFlagNames);
        assertFlags(openFlags, openFlagNames);
        assertFlags(flags, descriptorFlagNames);
        if (openFlags.create || openFlags.truncate || flags.write || flags.mutateDirectory) {
          throw "read-only";
        }
        const selectedKind = resolvePath(path, fileName);
        if (openFlags.exclusive) throw "exist";
        if (openFlags.directory && selectedKind !== "directory") throw "not-directory";
        return createDescriptor(selectedKind, flags.read === true);
      },
      read(length, offset) {
        assertAlive();
        if (kind === "directory") throw "is-directory";
        if (!canRead) throw "bad-descriptor";
        assertSize(length);
        assertSize(offset);
        const size = BigInt(contents.byteLength);
        const start = offset > size ? size : offset;
        const end = length > size - start ? size : start + length;
        return [contents.slice(Number(start), Number(end)), end === size];
      },
      getType() {
        assertAlive();
        return kind;
      },
      getFlags() {
        assertAlive();
        return { read: canRead };
      },
      isSameObject(other) {
        assertAlive();
        return identities.has(other);
      },
      [resourceDisposeSymbol]() { disposed = true; },
      write: denyMutation,
      writeViaStream: denyMutation,
      appendViaStream: denyMutation,
      setSize: denyMutation,
      setTimes: denyMutation,
      createDirectoryAt: denyMutation,
      setTimesAt: denyMutation,
      linkAt: denyMutation,
      removeDirectoryAt: denyMutation,
      renameAt: denyMutation,
      symlinkAt: denyMutation,
      unlinkFileAt: denyMutation,
      readViaStream: unsupported,
      advise: unsupported,
      sync: unsupported,
      syncData: unsupported,
      readDirectory: unsupported,
      stat: unsupported,
      statAt: unsupported,
      readlinkAt: unsupported,
      metadataHash: unsupported,
      metadataHashAt: unsupported,
    });
    identities.add(descriptor);
    return descriptor;
  }

  return Object.freeze({
    directory: createDescriptor("directory", true),
    release() { contents = null; },
  });
}

function assertFlags(value, names) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.keys(value).some(name => !names.includes(name)
        || typeof value[name] !== "boolean")) {
    throw "invalid";
  }
}

function assertSize(value) {
  if (typeof value !== "bigint" || value < 0n || value > maximumSize) throw "invalid";
}

function resolvePath(path, fileName) {
  if (typeof path !== "string" || path.includes("\0")) throw "invalid";
  if (path.length === 0) throw "no-entry";
  if (path.startsWith("/")) throw "not-permitted";
  let kind = "directory";
  for (const segment of path.split("/")) {
    if (kind !== "directory") throw "not-directory";
    if (segment === "" || segment === ".") continue;
    if (segment === "..") throw "not-permitted";
    if (segment !== fileName) throw "no-entry";
    kind = "regular-file";
  }
  return kind;
}
