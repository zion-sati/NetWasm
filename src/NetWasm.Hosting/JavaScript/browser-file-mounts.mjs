// A browser host's file system (#142), Emscripten-style: an in-memory tree that answers Node's
// synchronous fs calls, so a guest's file call completes without a wait in any browser, with
// or without JSPI. What the user picked is mounted into it:
// - a File or Blob, read-only: in a worker it is read in place (readBlobSync, FileReaderSync),
//   on the page it is copied in when mounted;
// - a FileSystemFileHandle or FileSystemDirectoryHandle (the Origin Private File System's root
//   among them), read-write: copied in when mounted and written back by sync().
// Everything else in the tree lives in memory for the host's lifetime; it starts with an empty
// /tmp, where Path.GetTempPath() points.

const optionKeys = ["readBlobSync"];
const now = () => Date.now();

export function createBrowserFileMounts(options) {
  assertExactDataObject(options, optionKeys, "browser file mount options");
  if (options.readBlobSync !== null && typeof options.readBlobSync !== "function") {
    throw new TypeError("browser file mount readBlobSync must be a function or null");
  }
  const root = directory();
  const temporary = directory();
  temporary.parentNode = root;
  temporary.nameInParent = "tmp";
  root.children.set("tmp", temporary);
  const files = new Map();
  let nextFile = 3;

  // Resolves a canonical absolute path to its node; links are followed except, when lstat
  // asks, the last segment's. The parent and name are kept for changes.
  function resolve(path, followLast = true, depth = 0) {
    const segments = readPath(path);
    let node = root;
    let parent = null;
    for (let index = 0; index < segments.length; index++) {
      if (node.kind !== "directory") throw failure("ENOTDIR");
      parent = node;
      node = node.children.get(segments[index]);
      if (node === undefined) {
        if (index === segments.length - 1) return { node: null, parent, name: segments[index] };
        throw failure("ENOENT");
      }
      if (node.kind === "link" && (followLast || index < segments.length - 1)) {
        if (depth > 32) throw failure("ELOOP");
        const target = node.target.startsWith("/")
          ? node.target
          : `/${[...segments.slice(0, index), node.target].join("/")}`;
        const rest = segments.slice(index + 1);
        return resolve(normalize(rest.length === 0 ? target : `${target}/${rest.join("/")}`), followLast, depth + 1);
      }
    }
    return { node, parent, name: segments.at(-1) ?? "" };
  }

  function existing(path, followLast = true) {
    const found = resolve(path, followLast);
    if (found.node === null) throw failure("ENOENT");
    return found;
  }

  function writable(node) {
    if (node.readOnly) throw failure("EROFS");
    return node;
  }

  function changed(node) {
    node.mtimeMs = node.ctimeMs = now();
    node.dirty = true;
  }

  const hostFiles = Object.freeze({
    lstatSync: path => stats(existing(path, false).node),
    statSync: path => stats(existing(path).node),
    realpathSync: path => pathOf(existing(path).node),
    openSync(path, flags) {
      const found = resolve(path);
      let node = found.node;
      if (flags === "wx" && node !== null) throw failure("EEXIST");
      if (node === null) {
        if (flags === "r" || flags === "r+") throw failure("ENOENT");
        node = file(new Uint8Array(0));
        writable(found.parent).children.set(found.name, node);
        node.parentNode = found.parent;
        node.nameInParent = found.name;
        changed(found.parent);
      }
      if (node.kind === "directory") throw failure("EISDIR");
      if (flags !== "r") writable(node);
      const handle = nextFile++;
      files.set(handle, node);
      return handle;
    },
    closeSync(handle) {
      if (!files.delete(handle)) throw failure("EBADF");
    },
    readSync(handle, buffer, offset, length, position) {
      const node = open(handle);
      const start = Math.min(position, size(node));
      const end = Math.min(start + length, size(node));
      const bytes = node.blob !== undefined ? options.readBlobSync(node.blob, start, end) : node.data.subarray(start, end);
      buffer.set(bytes, offset);
      node.atimeMs = now();
      return bytes.byteLength;
    },
    writeSync(handle, buffer, offset, length, position) {
      const node = open(handle);
      const at = position ?? node.data.byteLength;
      grow(node, at + length);
      node.data.set(buffer.subarray(offset, offset + length), at);
      changed(node);
      return length;
    },
    ftruncateSync(handle, length) {
      const node = open(handle);
      if (length > node.data.byteLength) grow(node, length);
      else node.data = node.data.slice(0, length);
      changed(node);
    },
    mkdirSync(path) {
      const found = resolve(path, false);
      if (found.node !== null) throw failure("EEXIST");
      const made = directory();
      made.parentNode = writable(found.parent);
      made.nameInParent = found.name;
      found.parent.children.set(found.name, made);
      changed(found.parent);
    },
    rmdirSync(path) {
      const { node, parent, name } = existing(path, false);
      if (node.kind !== "directory") throw failure("ENOTDIR");
      if (node.children.size !== 0) throw failure("ENOTEMPTY");
      if (node.mount !== undefined || parent === null) throw failure("EBUSY");
      writable(parent).children.delete(name);
      changed(parent);
    },
    unlinkSync(path) {
      const { node, parent, name } = existing(path, false);
      if (node.kind === "directory") throw failure("EISDIR");
      if (node.mount !== undefined) throw failure("EBUSY");
      writable(parent).children.delete(name);
      changed(parent);
    },
    renameSync(from, to) {
      const source = existing(from, false);
      const target = resolve(to, false);
      if (source.node.mount !== undefined || target.node?.mount !== undefined) throw failure("EBUSY");
      if (target.node !== null && target.node.kind === "directory") {
        if (source.node.kind !== "directory") throw failure("EISDIR");
        if (target.node.children.size !== 0) throw failure("ENOTEMPTY");
      }
      writable(source.parent);
      writable(target.parent);
      for (let ancestor = target.parent; ancestor !== null; ancestor = ancestor.parentNode) {
        if (ancestor === source.node) throw failure("EINVAL");
      }
      source.parent.children.delete(source.name);
      target.parent.children.set(target.name, source.node);
      source.node.parentNode = target.parent;
      source.node.nameInParent = target.name;
      source.node.dirty = true;
      changed(source.parent);
      changed(target.parent);
    },
    readdirSync(path) {
      const { node } = existing(path);
      if (node.kind !== "directory") throw failure("ENOTDIR");
      return [...node.children].map(([name, child]) => Object.freeze({
        name,
        isDirectory: () => child.kind === "directory",
        isFile: () => child.kind === "file",
        isSymbolicLink: () => child.kind === "link",
      }));
    },
    readlinkSync(path) {
      const { node } = existing(path, false);
      if (node.kind !== "link") throw failure("EINVAL");
      return node.target;
    },
    symlinkSync(target, path) {
      const found = resolve(path, false);
      if (found.node !== null) throw failure("EEXIST");
      const link = { kind: "link", target, readOnly: false, ...times() };
      link.parentNode = writable(found.parent);
      link.nameInParent = found.name;
      found.parent.children.set(found.name, link);
      changed(found.parent);
    },
    utimesSync(path, access, modification) {
      const node = writable(existing(path).node);
      node.atimeMs = access * 1000;
      node.mtimeMs = modification * 1000;
      node.ctimeMs = now();
    },
  });

  function open(handle) {
    const node = files.get(handle);
    if (node === undefined) throw failure("EBADF");
    return node;
  }

  // Makes the parents of a mount point, as `mkdir -p` would.
  function place(guestPath, node) {
    const segments = readPath(guestPath);
    if (segments.length === 0) throw new TypeError("a mount needs a path below /");
    let parent = root;
    for (const segment of segments.slice(0, -1)) {
      let next = parent.children.get(segment);
      if (next === undefined) {
        next = directory();
        next.parentNode = parent;
        next.nameInParent = segment;
        parent.children.set(segment, next);
      }
      if (next.kind !== "directory") throw new TypeError(`'${guestPath}' is not below a directory`);
      parent = next;
    }
    const name = segments.at(-1);
    if (parent.children.has(name)) throw new TypeError(`'${guestPath}' is already in use`);
    node.parentNode = parent;
    node.nameInParent = name;
    parent.children.set(name, node);
  }

  async function load(source, guestPath) {
    if (isBlob(source)) {
      const node = options.readBlobSync === null
        ? file(new Uint8Array(await source.arrayBuffer()))
        : { ...file(new Uint8Array(0)), blob: source };
      node.readOnly = true;
      if (typeof source.lastModified === "number") node.mtimeMs = source.lastModified;
      return node;
    }
    if (isHandle(source, "file")) {
      const picked = await source.getFile();
      const node = file(new Uint8Array(await picked.arrayBuffer()));
      node.handle = source;
      node.mtimeMs = picked.lastModified;
      return node;
    }
    if (isHandle(source, "directory")) return loadDirectory(source);
    throw new TypeError(`'${guestPath}' must mount a File, Blob, FileSystemFileHandle or FileSystemDirectoryHandle`);
  }

  async function loadDirectory(handle) {
    const node = directory();
    node.handle = handle;
    for await (const [name, child] of handle.entries()) {
      const loaded = child.kind === "directory" ? await loadDirectory(child) : await load(child, name);
      loaded.parentNode = node;
      loaded.nameInParent = name;
      node.children.set(name, loaded);
    }
    return node;
  }

  // Writes a mounted handle's tree back: changed files, new directories and files, and
  // removed entries. Links have no handle form and are left out.
  async function save(node, handle) {
    if (node.kind === "file") {
      if (!node.dirty) return;
      if (typeof handle.createWritable !== "function") {
        throw new TypeError("this browser cannot write a file handle back (no createWritable)");
      }
      const stream = await handle.createWritable();
      await stream.write(node.data.slice());
      await stream.close();
      node.dirty = false;
      return;
    }
    const present = new Set();
    for await (const [name] of handle.entries()) present.add(name);
    for (const name of present) {
      const child = node.children.get(name);
      if (child === undefined || child.kind === "link") await handle.removeEntry(name, { recursive: true });
    }
    for (const [name, child] of node.children) {
      if (child.kind === "link") continue;
      const childHandle = child.kind === "directory"
        ? await handle.getDirectoryHandle(name, { create: true })
        : await handle.getFileHandle(name, { create: true });
      if (child.kind === "file" && !present.has(name)) child.dirty = true;
      await save(child, childHandle);
    }
    node.dirty = false;
  }

  const mounts = new Map();

  return Object.freeze({
    hostFiles,
    // Mounts what the user picked at a canonical absolute path; the promise settles once it is
    // in place, so the guest sees it from its next file call.
    async mount(guestPath, source) {
      readPath(guestPath);
      if (mounts.has(guestPath)) throw new TypeError(`'${guestPath}' is already mounted`);
      const node = await load(source, guestPath);
      node.mount = source;
      place(guestPath, node);
      mounts.set(guestPath, node);
    },
    unmount(guestPath) {
      const node = mounts.get(guestPath);
      if (node === undefined) throw new TypeError(`'${guestPath}' is not mounted`);
      mounts.delete(guestPath);
      node.parentNode.children.delete(node.nameInParent);
    },
    // Writes a handle mount's changes back to what the user picked.
    async sync(guestPath) {
      const node = mounts.get(guestPath);
      if (node === undefined) throw new TypeError(`'${guestPath}' is not mounted`);
      if (node.handle === undefined) throw new TypeError(`'${guestPath}' is read-only`);
      await save(node, node.handle);
    },
    // The mounts, for a worker that should see them too.
    list: () => Object.freeze([...mounts].map(([guestPath, node]) => Object.freeze({ guestPath, source: node.mount }))),
  });
}

function times() {
  const at = now();
  return { atimeMs: at, mtimeMs: at, ctimeMs: at };
}

function directory() {
  return { kind: "directory", children: new Map(), readOnly: false, parentNode: null, nameInParent: "", ...times() };
}

function file(data) {
  return { kind: "file", data, readOnly: false, dirty: false, ...times() };
}

function size(node) {
  return node.blob !== undefined ? node.blob.size : node.data.byteLength;
}

function grow(node, length) {
  if (length <= node.data.byteLength) return;
  const data = new Uint8Array(length);
  data.set(node.data);
  node.data = data;
}

function stats(node) {
  return Object.freeze({
    isDirectory: () => node.kind === "directory",
    isFile: () => node.kind === "file",
    isSymbolicLink: () => node.kind === "link",
    nlink: 1,
    size: node.kind === "file" ? size(node) : node.kind === "link" ? node.target.length : 0,
    atimeMs: node.atimeMs,
    mtimeMs: node.mtimeMs,
    ctimeMs: node.ctimeMs,
  });
}

function pathOf(node) {
  const names = [];
  for (let current = node; current.parentNode !== null; current = current.parentNode) names.unshift(current.nameInParent);
  return `/${names.join("/")}`;
}

function isBlob(value) {
  return typeof Blob === "function" && value instanceof Blob;
}

function isHandle(value, kind) {
  return value !== null && typeof value === "object" && value.kind === kind
    && typeof value.getFile === (kind === "file" ? "function" : "undefined")
    && (kind === "file" || typeof value.entries === "function");
}

// The segments of a canonical absolute path ("/" has none).
function readPath(path) {
  if (typeof path !== "string" || !path.startsWith("/") || path.includes("\0") || path.includes("\\")) {
    throw new TypeError("a canonical absolute path is required");
  }
  const segments = path.split("/").filter(segment => segment !== "");
  if (segments.some(segment => segment === "." || segment === "..")) {
    throw new TypeError("a canonical absolute path is required");
  }
  return segments;
}

function normalize(path) {
  const segments = [];
  for (const segment of path.split("/")) {
    if (segment === "" || segment === ".") continue;
    if (segment === "..") segments.pop();
    else segments.push(segment);
  }
  return `/${segments.join("/")}`;
}

function failure(code) {
  return Object.assign(new Error(code), { code });
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
