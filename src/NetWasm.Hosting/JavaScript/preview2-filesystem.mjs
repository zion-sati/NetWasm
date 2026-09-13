import { createReadOnlyFileDirectory } from "./read-only-file-directory.mjs";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";

// The versioned resource API, not an application import inventory. The selected
// provider's implemented subset is preserved; final imports are projected later.
const descriptorMethods = Object.freeze([
  "readViaStream", "writeViaStream", "appendViaStream", "advise", "syncData",
  "getFlags", "getType", "setSize", "setTimes", "read", "write", "readDirectory",
  "sync", "createDirectoryAt", "stat", "statAt", "setTimesAt", "linkAt", "openAt",
  "readlinkAt", "removeDirectoryAt", "renameAt", "symlinkAt", "unlinkFileAt",
  "isSameObject", "metadataHash", "metadataHashAt",
]);
const mutatingMethods = new Set([
  "writeViaStream", "appendViaStream", "setSize", "setTimes", "write",
  "createDirectoryAt", "setTimesAt", "removeDirectoryAt", "symlinkAt", "unlinkFileAt",
]);

export function createPreview2Filesystem({ filesystem, preopenAccess } = {}) {
  const SourceDescriptor = filesystem?.types?.Descriptor;
  const getDirectories = filesystem?.preopens?.getDirectories;
  if (typeof SourceDescriptor !== "function" || !SourceDescriptor.prototype
      || typeof SourceDescriptor.prototype.openAt !== "function"
      || typeof SourceDescriptor.prototype.read !== "function"
      || typeof getDirectories !== "function") {
    throw new TypeError("a Preview 2 filesystem with descriptor open/read and preopens is required");
  }
  const methods = descriptorMethods.filter(name => typeof SourceDescriptor.prototype[name] === "function");
  const access = readPreopenAccess(preopenAccess);
  const states = new WeakMap();
  const outstanding = new Set();
  const entries = new Map();
  const mounts = new Set();
  const constructionKey = Symbol("filesystem descriptor");
  let disposed = false;
  let sealed = false;

  class Descriptor {
    constructor(key) {
      if (key !== constructionKey) throw new TypeError("filesystem descriptors are created by the provider");
    }

    [resourceDisposeSymbol]() {
      const state = states.get(this);
      if (state.closed) return;
      state.closed = true;
      outstanding.delete(this);
      if (state.owned) state.release?.();
    }
  }

  function readState(descriptor) {
    const state = states.get(descriptor);
    if (!state || disposed || state.closed) throw "bad-descriptor";
    return state;
  }

  function captureDescriptor(backing, owner, owned) {
    if (backing === null || typeof backing !== "object"
        || !owner.internal && !(backing instanceof SourceDescriptor)) {
      throw new TypeError("the filesystem returned an invalid descriptor");
    }
    const actions = Object.create(null);
    for (const name of methods) {
      const action = backing[name];
      if (typeof action !== "function") throw new TypeError("the filesystem descriptor is incomplete");
      actions[name] = action.bind(backing);
    }
    const release = backing[resourceDisposeSymbol];
    if (release !== undefined && typeof release !== "function") {
      throw new TypeError("the filesystem descriptor disposer is invalid");
    }
    return {
      backing, owner, owned, actions,
      release: release?.bind(backing),
    };
  }

  function wrap(snapshot) {
    const descriptor = new Descriptor(constructionKey);
    states.set(descriptor, { ...snapshot, closed: false });
    outstanding.add(descriptor);
    return descriptor;
  }

  const specialMethods = {
    openAt(state, args) {
      if (state.owner.readOnly && requestsMutation(args[2], args[3])) throw "read-only";
      const backing = state.actions.openAt(...args);
      try {
        return wrap(captureDescriptor(backing, state.owner, true));
      } catch (error) {
        try { backing?.[resourceDisposeSymbol]?.(); } catch (cleanupError) {
          throw new AggregateError([error, cleanupError], "filesystem descriptor adoption failed");
        }
        throw error;
      }
    },
    isSameObject(state, [other]) {
      const otherState = readState(other);
      return state.owner === otherState.owner
        && state.actions.isSameObject(otherState.backing);
    },
    linkAt(state, [pathFlags, oldPath, other, newPath]) {
      const otherState = readState(other);
      if (state.owner.readOnly || otherState.owner.readOnly) throw "read-only";
      return state.actions.linkAt(pathFlags, oldPath, otherState.backing, newPath);
    },
    renameAt(state, [oldPath, other, newPath]) {
      const otherState = readState(other);
      if (state.owner.readOnly || otherState.owner.readOnly) throw "read-only";
      return state.actions.renameAt(oldPath, otherState.backing, newPath);
    },
    getFlags(state, args) {
      const flags = state.actions.getFlags(...args);
      if (!state.owner.readOnly || flags === null || typeof flags !== "object") return flags;
      return Object.freeze({ ...flags, write: false, mutateDirectory: false });
    },
  };
  for (const name of methods) {
    const invoke = specialMethods[name] ?? ((state, args) => {
      if (state.owner.readOnly && mutatingMethods.has(name)) throw "read-only";
      return state.actions[name](...args);
    });
    Object.defineProperty(Descriptor.prototype, name, {
      value(...args) { return invoke(readState(this), args); },
    });
  }
  Object.freeze(Descriptor.prototype);
  Object.freeze(Descriptor);

  const initialEntries = getDirectories.call(filesystem.preopens);
  if (!Array.isArray(initialEntries)) throw new TypeError("filesystem preopens must be an array");
  for (const entry of initialEntries) {
    if (!Array.isArray(entry) || entry.length !== 2) throw new TypeError("a filesystem preopen is invalid");
    const [backing, path] = entry;
    assertPath(path, true);
    if (entries.has(path)) throw new TypeError("a filesystem preopen path is duplicated");
    const readOnly = access === null ? false : access.get(path);
    if (readOnly === undefined) {
      throw new TypeError("filesystem preopens do not match the requested access grants");
    }
    entries.set(path, captureDescriptor(backing, { internal: false, closed: false, readOnly }, false));
  }
  if (access !== null && access.size !== entries.size) {
    throw new TypeError("filesystem preopens do not match the requested access grants");
  }

  function releaseMount(owner) {
    if (owner.closed) return;
    owner.closed = true;
    entries.delete(owner.path);
    mounts.delete(owner);
    owner.release();
    for (const descriptor of outstanding) {
      const state = states.get(descriptor);
      if (state.owner === owner) {
        state.closed = true;
        outstanding.delete(descriptor);
      }
    }
  }

  const types = { ...filesystem.types, Descriptor };
  if (typeof types.filesystemErrorCode === "function") {
    types.filesystemErrorCode = types.filesystemErrorCode.bind(filesystem.types);
  }
  return Object.freeze({
    types: Object.freeze(types),
    preopens: Object.freeze({
      getDirectories() {
        if (disposed) throw new Error("the filesystem has been disposed");
        sealed = true;
        return Array.from(entries, ([path, snapshot]) => [wrap(snapshot), path]);
      },
    }),
    mountReadOnlyFile({ guestPath, bytes } = {}) {
      if (disposed || sealed) throw new Error("filesystem mount setup is closed");
      assertPath(guestPath, false);
      const separator = guestPath.lastIndexOf("/");
      const path = guestPath.slice(0, separator) || "/";
      if (entries.has(path)) throw new TypeError("the read-only mount collides with an existing preopen");
      const resource = createReadOnlyFileDirectory({ fileName: guestPath.slice(separator + 1), bytes });
      const owner = { internal: true, closed: false, readOnly: true, path, release: resource.release };
      entries.set(path, captureDescriptor(resource.directory, owner, false));
      mounts.add(owner);
      return () => releaseMount(owner);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const owner of mounts) releaseMount(owner);
      const errors = [];
      for (const descriptor of outstanding) {
        try { descriptor[resourceDisposeSymbol](); } catch (error) { errors.push(error); }
      }
      entries.clear();
      if (errors.length !== 0) throw new AggregateError(errors, "filesystem descriptor cleanup failed");
    },
  });
}

function readPreopenAccess(value) {
  if (value === undefined) return null;
  if (!Array.isArray(value) || !Object.isFrozen(value)) {
    throw new TypeError("filesystem preopen access grants must be an immutable array");
  }
  const result = new Map();
  for (const grant of value) {
    if (grant === null || typeof grant !== "object" || Array.isArray(grant)
        || !Object.isFrozen(grant)
        || typeof grant.guestPath !== "string"
        || grant.access !== "readOnly" && grant.access !== "readWrite"
        || result.has(grant.guestPath)) {
      throw new TypeError("filesystem preopen access grant is invalid");
    }
    result.set(grant.guestPath, grant.access === "readOnly");
  }
  return result;
}

function requestsMutation(openFlags, descriptorFlags) {
  return readBooleanFlag(openFlags, "create") || readBooleanFlag(openFlags, "truncate")
    || readBooleanFlag(descriptorFlags, "write")
    || readBooleanFlag(descriptorFlags, "mutateDirectory");
}

function readBooleanFlag(value, name) {
  const descriptor = value !== null && typeof value === "object"
    ? Object.getOwnPropertyDescriptor(value, name)
    : undefined;
  return descriptor !== undefined && "value" in descriptor && descriptor.value === true;
}

function assertPath(path, allowRoot) {
  if (typeof path !== "string" || !path.startsWith("/") || /[\\\0]/u.test(path)
      || path !== "/" && path.slice(1).split("/").some(segment => segment === ""
        || segment === "." || segment === "..")
      || path === "/" && !allowRoot) {
    throw new TypeError("a canonical absolute WASI path is required");
  }
}
