import { createGuestByteReader, dropGuestStream } from "./output-sink-stream.mjs";
import { createSyncFuture, createSyncStream, readSyncDrain } from "./sync-values.mjs";

// One execution's wasi:filesystem 0.3 view: the directories the request grants (on hosts
// with a file system) and the read-only files the host mounts, such as time-zone data. A
// read-write grant lets the guest create, write, rename and remove files and directories
// inside it; nothing the guest names may resolve outside the granted directory.
//
// The view answers synchronously over a host file system with Node's synchronous fs API (Node's
// own, or a browser's in-memory one), so a call completes before the guest would wait: hosts
// that cannot suspend the guest (raw hosts) run file APIs too (Emscripten's NODEFS model).

const requestKeys = ["hostFiles", "preopens"];
const preopenKeys = ["access", "guestPath", "hostPath"];
const hostFileKeys = [
  "closeSync", "ftruncateSync", "lstatSync", "mkdirSync", "openSync", "readSync", "readdirSync",
  "readlinkSync", "realpathSync", "renameSync", "rmdirSync", "statSync", "symlinkSync", "unlinkSync",
  "utimesSync", "writeSync",
];
const chunkSize = 64 * 1024;
const constructionKey = Symbol("filesystem descriptor");

export function createWasiFilesystem(request) {
  assertExactDataObject(request, requestKeys, "WASI filesystem request");
  const hostFiles = readHostFiles(request.hostFiles);
  const entries = new Map();
  for (const preopen of readPreopens(request.preopens, hostFiles)) {
    entries.set(preopen.guestPath, Object.freeze({
      kind: "directory",
      host: preopen.hostPath,
      root: preopen.hostPath,
      readOnly: preopen.access === "readOnly",
    }));
  }
  const states = new WeakMap();
  const openFiles = new Set();
  let sealed = false;
  let disposed = false;
  // The read-only directories mountReadOnlyFile created, which later sidecars may join.
  const mountedDirectories = new WeakSet();

  class Descriptor {
    constructor(key) {
      if (key !== constructionKey) {
        throw new TypeError("filesystem descriptors are created by the provider");
      }
    }

    readViaStream(offset) {
      const node = readNode(this);
      if (node === null) return [[], settled(err("bad-descriptor"))];
      if (node.kind !== "file") return [[], settled(err("is-directory"))];
      const outcome = { result: ok(undefined) };
      const start = Number(BigInt.asUintN(64, offset));
      return [createSyncStream(readFile(node, start, outcome)), createSyncFuture(() => outcome.result)];
    }

    writeViaStream(data, offset) {
      return writeStream(this, data, Number(BigInt.asUintN(64, offset)));
    }

    appendViaStream(data) {
      return writeStream(this, data, null);
    }

    readDirectory() {
      const node = readNode(this);
      if (node === null) return [[], settled(err("bad-descriptor"))];
      if (node.kind !== "directory") return [[], settled(err("not-directory"))];
      const outcome = { result: ok(undefined) };
      return [createSyncStream(listDirectory(node, outcome)), createSyncFuture(() => outcome.result)];
    }

    getType() {
      const node = readNode(this);
      return node === null ? err("bad-descriptor") : ok(typeOf(node));
    }

    getFlags() {
      const state = readState(this);
      if (state === null) return err("bad-descriptor");
      return ok(Object.freeze(state.writable ? { read: true, write: true } : { read: true }));
    }

    stat() {
      const node = readNode(this);
      if (node === null) return err("bad-descriptor");
      if (node.host === undefined) return ok(memoryStat(node));
      return hostCall(() => ok(hostStat(hostFiles.statSync(node.host))));
    }

    statAt(pathFlags, path) {
      return withPath(this, path, false, (directory, segments) => {
        if (directory.host === undefined) {
          const target = openMemory(directory, segments);
          return typeof target === "string" ? err(target) : ok(memoryStat(target));
        }
        if (flagSet(pathFlags, "symlinkFollow")) {
          const target = openHost(directory, segments, true);
          return typeof target === "string" ? err(target) : ok(hostStat(hostFiles.statSync(target.host)));
        }
        return ok(hostStat(hostFiles.lstatSync(hostChild(directory, segments))));
      });
    }

    openAt(pathFlags, path, openFlags, flags) {
      const node = readNode(this);
      if (node === null) return err("bad-descriptor");
      if (node.kind !== "directory") return err("not-directory");
      if (requestsMutation(openFlags, flags) && (node.readOnly || node.host === undefined)) {
        return err("read-only");
      }
      const segments = readRelativePath(path);
      if (segments === null) return err("not-permitted");
      const follow = flagSet(pathFlags, "symlinkFollow");
      let target;
      try {
        target = node.host === undefined
          ? openMemory(node, segments)
          : openHostForWrite(node, segments, follow, openFlags);
      } catch (error) {
        return err(errorCode(error));
      }
      if (typeof target === "string") return err(target);
      if (flagSet(openFlags, "directory") && target.kind !== "directory") return err("not-directory");
      const writable = flagSet(flags, "write");
      if (writable && target.kind === "directory") return err("is-directory");
      // A file the host will not write, such as a read-only one, fails when opened for writing,
      // as on desktop, rather than at its first write.
      if (writable && target.host !== undefined) {
        try {
          withHandle(target.host, "r+", () => {});
        } catch (error) {
          return err(errorCode(error));
        }
      }
      return ok(wrap(target, writable));
    }

    setSize(size) {
      return withWritableFile(this, node => {
        withHandle(node.host, "r+", file => hostFiles.ftruncateSync(file, Number(BigInt.asUintN(64, size))));
      });
    }

    sync() {
      return readNode(this) === null ? err("bad-descriptor") : ok(undefined);
    }

    syncData() {
      return this.sync();
    }

    advise() {
      return this.sync();
    }

    setTimes(dataAccessTimestamp, dataModificationTimestamp) {
      const node = readNode(this);
      if (node === null) return err("bad-descriptor");
      if (node.host === undefined || node.readOnly) return err("read-only");
      return hostCall(() => setHostTimes(node.host, dataAccessTimestamp, dataModificationTimestamp));
    }

    setTimesAt(pathFlags, path, dataAccessTimestamp, dataModificationTimestamp) {
      return withPath(this, path, true, (directory, segments) => {
        const target = openHost(directory, segments, flagSet(pathFlags, "symlinkFollow"));
        if (typeof target === "string") return err(target);
        setHostTimes(target.host, dataAccessTimestamp, dataModificationTimestamp);
        return ok(undefined);
      });
    }

    createDirectoryAt(path) {
      return withPath(this, path, true, (directory, segments) => {
        hostFiles.mkdirSync(hostChild(directory, segments));
        return ok(undefined);
      });
    }

    removeDirectoryAt(path) {
      return withPath(this, path, true, (directory, segments) => {
        hostFiles.rmdirSync(hostChild(directory, segments));
        return ok(undefined);
      });
    }

    unlinkFileAt(path) {
      return withPath(this, path, true, (directory, segments) => {
        const child = hostChild(directory, segments);
        if (hostFiles.lstatSync(child).isDirectory()) return err("is-directory");
        hostFiles.unlinkSync(child);
        return ok(undefined);
      });
    }

    renameAt(oldPath, newDescriptor, newPath) {
      const destination = readNode(newDescriptor);
      if (destination === null) return err("bad-descriptor");
      if (destination.kind !== "directory") return err("not-directory");
      if (destination.host === undefined || destination.readOnly) return err("read-only");
      const newSegments = readRelativePath(newPath);
      if (newSegments === null || newSegments.length === 0) return err("not-permitted");
      return withPath(this, oldPath, true, (directory, segments) => {
        hostFiles.renameSync(hostChild(directory, segments), hostChild(destination, newSegments));
        return ok(undefined);
      });
    }

    symlinkAt(oldPath, newPath) {
      if (typeof oldPath !== "string" || oldPath.includes("\0")) return err("invalid");
      return withPath(this, newPath, true, (directory, segments) => {
        hostFiles.symlinkSync(oldPath, hostChild(directory, segments));
        return ok(undefined);
      });
    }

    readlinkAt(path) {
      return withPath(this, path, false, (directory, segments) => {
        if (directory.host === undefined) return err("invalid");
        return ok(hostFiles.readlinkSync(hostChild(directory, segments)));
      });
    }

    isSameObject(other) {
      const node = readNode(this);
      return node !== null && node === states.get(other)?.node;
    }
  }
  for (const name of ["linkAt", "metadataHash", "metadataHashAt"]) {
    Object.defineProperty(Descriptor.prototype, name, {
      value: function unsupported() {
        return readNode(this) === null ? err("bad-descriptor") : err("unsupported");
      },
    });
  }

  function wrap(node, writable = false) {
    const descriptor = new Descriptor(constructionKey);
    states.set(descriptor, { node, writable });
    return descriptor;
  }

  function readState(descriptor) {
    return disposed ? null : states.get(descriptor) ?? null;
  }

  function readNode(descriptor) {
    return readState(descriptor)?.node ?? null;
  }

  // Runs a path operation on a directory descriptor: a change needs a read-write host
  // directory, and the path must stay inside it and name something below it.
  function withPath(descriptor, path, mutates, action) {
    const node = readNode(descriptor);
    if (node === null) return err("bad-descriptor");
    if (node.kind !== "directory") return err("not-directory");
    if (mutates && (node.host === undefined || node.readOnly)) return err("read-only");
    const segments = readRelativePath(path);
    if (segments === null) return err("not-permitted");
    if (mutates && segments.length === 0) return err("invalid");
    return hostCall(() => action(node, segments));
  }

  function withWritableFile(descriptor, action) {
    const state = readState(descriptor);
    if (state === null) return err("bad-descriptor");
    if (state.node.kind !== "file") return err("is-directory");
    if (!state.writable) return err("bad-descriptor");
    return hostCall(() => action(state.node));
  }

  function hostCall(action) {
    try {
      const result = action();
      return result === undefined ? ok(undefined) : result;
    } catch (error) {
      return err(errorCode(error));
    }
  }

  function withHandle(path, flags, action) {
    const file = openTracked(path, flags);
    try {
      return action(file);
    } finally {
      closeTracked(file);
    }
  }

  // Copies a guest stream into the file at the offset (null: its end). A raw host's buffered
  // stream is copied when the guest reads the result, which is after it finished writing; any
  // other stream, and the rest of one the guest is still writing, is copied as it arrives. A
  // failure stops reading the stream.
  function writeStream(descriptor, data, offset) {
    const state = readState(descriptor);
    const refusal = state === null ? "bad-descriptor"
      : state.node.kind !== "file" ? "is-directory"
        : state.writable ? null : "bad-descriptor";
    if (refusal !== null) return settled(err(refusal));
    const drain = readSyncDrain(data);
    const position = { value: offset };
    const flags = offset === null ? "a" : "r+";
    const open = () => {
      try {
        return openTracked(state.node.host, flags);
      } catch (error) {
        dropGuestStream(data);
        return err(errorCode(error));
      }
    };
    if (drain === null) {
      const file = open();
      return typeof file === "object" ? Promise.resolve(file) : copyArriving(file, data, position);
    }
    return createSyncFuture(() => {
      const file = open();
      if (typeof file === "object") return file;
      try {
        for (let chunk = drain(); chunk !== null; chunk = drain()) {
          if (chunk === undefined) return copyArriving(file, data, position);
          writeChunk(file, bytesOf(chunk), position);
        }
      } catch (error) {
        closeTracked(file);
        dropGuestStream(data);
        return err(errorCode(error));
      }
      closeTracked(file);
      return ok(undefined);
    });
  }

  // Copies the rest of the stream into the open file as it arrives, closing the file at the end.
  async function copyArriving(file, data, position) {
    let result = ok(undefined);
    try {
      const read = createGuestByteReader(data);
      for (let chunk = await read(); chunk !== null; chunk = await read()) writeChunk(file, chunk, position);
    } catch (error) {
      dropGuestStream(data);
      result = err(errorCode(error));
    }
    closeTracked(file);
    return result;
  }

  function openTracked(path, flags) {
    const file = hostFiles.openSync(path, flags);
    openFiles.add(file);
    return file;
  }

  function closeTracked(file) {
    openFiles.delete(file);
    hostFiles.closeSync(file);
  }

  function writeChunk(file, chunk, position) {
    let written = 0;
    while (written < chunk.byteLength) {
      written += hostFiles.writeSync(file, chunk, written, chunk.byteLength - written,
        position.value === null ? null : position.value + written);
    }
    if (position.value !== null) position.value += chunk.byteLength;
  }

  function* readFile(node, start, outcome) {
    if (node.host === undefined) {
      for (let offset = start; offset < node.bytes.byteLength; offset += chunkSize) {
        yield node.bytes.slice(offset, Math.min(node.bytes.byteLength, offset + chunkSize));
      }
      return;
    }
    let file = null;
    try {
      file = hostFiles.openSync(node.host, "r");
      openFiles.add(file);
      let position = start;
      let bytesRead;
      do {
        const buffer = new Uint8Array(chunkSize);
        bytesRead = hostFiles.readSync(file, buffer, 0, chunkSize, position);
        position += bytesRead;
        if (bytesRead !== 0) yield buffer.subarray(0, bytesRead);
      } while (bytesRead !== 0);
    } catch (error) {
      outcome.result = err(errorCode(error));
    } finally {
      // Disposal closes a file a stream still reads; the stream then leaves it closed.
      if (file !== null && openFiles.delete(file)) {
        try {
          hostFiles.closeSync(file);
        } catch {
          // The file is gone with its handle.
        }
      }
    }
  }

  function* listDirectory(node, outcome) {
    if (node.host === undefined) {
      for (const name of node.files.keys()) {
        yield Object.freeze({ type: Object.freeze({ tag: "regular-file" }), name });
      }
      return;
    }
    let listed;
    try {
      listed = hostFiles.readdirSync(node.host, { withFileTypes: true });
    } catch (error) {
      outcome.result = err(errorCode(error));
      return;
    }
    for (const entry of listed) {
      yield Object.freeze({ type: entryType(entry), name: entry.name });
    }
  }

  // The host path of a path below a host directory, which need not exist: its parent must
  // resolve inside the directory's root, and its last segment is used as named.
  function hostChild(directory, segments) {
    const parent = hostFiles.realpathSync([directory.host, ...segments.slice(0, -1)].join("/"));
    if (!insideRoot(parent, directory.root)) throw Object.assign(new Error("escapes"), { code: "EPERM" });
    return `${parent}/${segments.at(-1)}`;
  }

  function insideRoot(real, root) {
    const realRoot = hostFiles.realpathSync(root);
    return real === realRoot || real.startsWith(realRoot.endsWith("/") ? realRoot : `${realRoot}/`);
  }

  function openHost(directory, segments, followSymlinks) {
    const path = [directory.host, ...segments].join("/");
    try {
      const link = hostFiles.lstatSync(path);
      if (link.isSymbolicLink() && !followSymlinks) return "loop";
      const real = hostFiles.realpathSync(path);
      if (!insideRoot(real, directory.root)) return "not-permitted";
      const status = hostFiles.statSync(real);
      if (!status.isDirectory() && !status.isFile()) return "unsupported";
      return Object.freeze({
        kind: status.isDirectory() ? "directory" : "file",
        host: real,
        root: directory.root,
        readOnly: directory.readOnly,
      });
    } catch (error) {
      return errorCode(error);
    }
  }

  // Opens as open-at's flags say: create makes a missing file (exclusive: only a missing one),
  // and truncate empties an existing one.
  function openHostForWrite(directory, segments, followSymlinks, openFlags) {
    if (flagSet(openFlags, "create") && segments.length !== 0) {
      const child = hostChild(directory, segments);
      const exclusive = flagSet(openFlags, "exclusive");
      if (exclusive || openHost(directory, segments, followSymlinks) === "no-entry") {
        hostFiles.closeSync(hostFiles.openSync(child, exclusive ? "wx" : "a"));
      }
    }
    const target = openHost(directory, segments, followSymlinks);
    if (typeof target !== "string" && target.kind === "file" && flagSet(openFlags, "truncate")) {
      withHandle(target.host, "r+", file => hostFiles.ftruncateSync(file, 0));
    }
    return target;
  }

  function setHostTimes(path, access, modification) {
    const current = access.tag === "no-change" || modification.tag === "no-change"
      ? hostFiles.statSync(path)
      : null;
    hostFiles.utimesSync(path, newTime(access, current?.atimeMs), newTime(modification, current?.mtimeMs));
  }

  return Object.freeze({
    types: Object.freeze({ Descriptor }),
    preopens: Object.freeze({
      getDirectories() {
        if (disposed) throw new Error("the filesystem has been disposed");
        sealed = true;
        return Array.from(entries, ([path, node]) => [wrap(node), path]);
      },
    }),
    // Mounts one read-only file at an absolute guest path before the guest first asks
    // for its directories; files mounted into the same directory share one read-only
    // preopen (a program's sidecars). The release removes the file, and the preopen
    // with its last file.
    mountReadOnlyFile({ guestPath, bytes } = {}) {
      if (disposed || sealed) throw new Error("filesystem mount setup is closed");
      assertAbsolutePath(guestPath);
      if (!(bytes instanceof Uint8Array)) throw new TypeError("read-only file contents must be bytes");
      const separator = guestPath.lastIndexOf("/");
      const directory = guestPath.slice(0, separator) || "/";
      const name = guestPath.slice(separator + 1);
      const existing = entries.get(directory);
      if (existing !== undefined && !mountedDirectories.has(existing)) {
        throw new TypeError("the read-only mount collides with an existing preopen");
      }
      const mounted = existing ?? Object.freeze({ kind: "directory", files: new Map(), readOnly: true });
      if (mounted.files.has(name)) throw new TypeError("the read-only file is already mounted");
      mounted.files.set(name, Object.freeze({ kind: "file", bytes: new Uint8Array(bytes), readOnly: true }));
      mountedDirectories.add(mounted);
      entries.set(directory, mounted);
      return () => {
        mounted.files.delete(name);
        if (mounted.files.size === 0 && entries.get(directory) === mounted) entries.delete(directory);
      };
    },
    async dispose() {
      if (disposed) return;
      disposed = true;
      entries.clear();
      for (const file of openFiles) {
        try {
          hostFiles.closeSync(file);
        } catch {
          // The file is gone with its handle.
        }
      }
      openFiles.clear();
    },
  });
}

function openMemory(directory, segments) {
  if (segments.length === 0) return directory;
  if (segments.length !== 1) return "no-entry";
  return directory.files.get(segments[0]) ?? "no-entry";
}

function typeOf(node) {
  return Object.freeze({ tag: node.kind === "directory" ? "directory" : "regular-file" });
}

function entryType(entry) {
  return Object.freeze(entry.isDirectory() ? { tag: "directory" }
    : entry.isFile() ? { tag: "regular-file" }
    : entry.isSymbolicLink() ? { tag: "symbolic-link" }
    : { tag: "other", val: undefined });
}

function memoryStat(node) {
  return Object.freeze({
    type: typeOf(node),
    linkCount: 1n,
    size: node.kind === "file" ? BigInt(node.bytes.byteLength) : 0n,
    dataAccessTimestamp: undefined,
    dataModificationTimestamp: undefined,
    statusChangeTimestamp: undefined,
  });
}

function hostStat(status) {
  return Object.freeze({
    type: Object.freeze({
      tag: status.isDirectory() ? "directory" : status.isSymbolicLink() ? "symbolic-link" : "regular-file",
    }),
    linkCount: BigInt(status.nlink),
    size: BigInt(status.size),
    dataAccessTimestamp: instant(status.atimeMs),
    dataModificationTimestamp: instant(status.mtimeMs),
    statusChangeTimestamp: instant(status.ctimeMs),
  });
}

function instant(milliseconds) {
  const seconds = Math.floor(milliseconds / 1000);
  return Object.freeze({
    seconds: BigInt(seconds),
    nanoseconds: Math.min(999_999_999, Math.floor((milliseconds - seconds * 1000) * 1_000_000)),
  });
}

// A new-timestamp as the seconds utimes takes: unchanged keeps the current time.
function newTime(timestamp, currentMilliseconds) {
  switch (timestamp.tag) {
    case "no-change":
      return currentMilliseconds / 1000;
    case "now":
      return Date.now() / 1000;
    default:
      return Number(timestamp.val.seconds) + timestamp.val.nanoseconds / 1_000_000_000;
  }
}

const errorCodes = new Map([
  ["EACCES", "access"], ["EPERM", "not-permitted"], ["EEXIST", "exist"],
  ["EISDIR", "is-directory"], ["ELOOP", "loop"], ["ENAMETOOLONG", "name-too-long"],
  ["ENOENT", "no-entry"], ["ENOTDIR", "not-directory"], ["EBUSY", "busy"],
  ["ENOTEMPTY", "not-empty"], ["EXDEV", "cross-device"], ["ENOSPC", "insufficient-space"],
  ["EROFS", "read-only"], ["EINVAL", "invalid"], ["EFBIG", "file-too-large"],
]);

function errorCode(error) {
  return errorCodes.get(error?.code) ?? "io";
}

function ok(val) {
  return Object.freeze({ tag: "ok", val });
}

function err(code) {
  return Object.freeze({ tag: "err", val: Object.freeze({ tag: code }) });
}

// A result the guest can read at once.
function settled(value) {
  return createSyncFuture(() => value);
}

function bytesOf(chunk) {
  return chunk instanceof Uint8Array ? chunk : Uint8Array.from(chunk);
}

function requestsMutation(openFlags, descriptorFlags) {
  return flagSet(openFlags, "create") || flagSet(openFlags, "truncate")
    || flagSet(openFlags, "exclusive") || flagSet(descriptorFlags, "write")
    || flagSet(descriptorFlags, "mutateDirectory");
}

function flagSet(value, name) {
  return value !== null && typeof value === "object" && value[name] === true;
}

// A guest path relative to its directory: "." segments drop, ".." may not leave the
// directory, and an absolute path is not permitted.
function readRelativePath(path) {
  if (typeof path !== "string" || path.startsWith("/") || path.includes("\0")) return null;
  const segments = [];
  for (const segment of path.split("/")) {
    if (segment === "" || segment === ".") continue;
    if (segment === "..") {
      if (segments.length === 0) return null;
      segments.pop();
    } else {
      segments.push(segment);
    }
  }
  return segments;
}

function assertAbsolutePath(path) {
  if (typeof path !== "string" || !path.startsWith("/") || path === "/" || /[\\\0]/u.test(path)
      || path.slice(1).split("/").some(segment => segment === "" || segment === "." || segment === "..")) {
    throw new TypeError("a canonical absolute WASI path is required");
  }
}

function readHostFiles(value) {
  if (value === null) return null;
  assertExactDataObject(value, hostFileKeys, "WASI host file system");
  if (Object.values(value).some(action => typeof action !== "function")) {
    throw new TypeError("WASI host file system actions are required");
  }
  return value;
}

function readPreopens(value, hostFiles) {
  if (!Array.isArray(value) || !Object.isFrozen(value)) {
    throw new TypeError("WASI preopen grants must be an immutable array");
  }
  if (value.length !== 0 && hostFiles === null) {
    throw new TypeError("this host cannot grant preopened directories");
  }
  const paths = new Set();
  return value.map(preopen => {
    assertExactDataObject(preopen, preopenKeys, "WASI preopen grant");
    if (!Object.isFrozen(preopen) || typeof preopen.hostPath !== "string"
        || !preopen.hostPath.startsWith("/") && !/^[A-Za-z]:[\\/]/u.test(preopen.hostPath)
        || typeof preopen.guestPath !== "string" || !preopen.guestPath.startsWith("/")
        || preopen.access !== "readOnly" && preopen.access !== "readWrite"
        || paths.has(preopen.guestPath)) {
      throw new TypeError("WASI preopen grant is invalid");
    }
    paths.add(preopen.guestPath);
    return preopen;
  });
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
