import { createPreview2Filesystem } from "./preview2-filesystem.mjs";

const requestKeys = ["createFilesystem", "preopens"];
const preopenKeys = ["access", "guestPath", "hostPath"];

export function createPreview2FilesystemScope(request) {
  assertExactDataObject(request, requestKeys, "Preview 2 filesystem scope request");
  if (typeof request.createFilesystem !== "function") {
    throw new TypeError("Preview 2 source filesystem factory is required");
  }
  if (!Array.isArray(request.preopens) || !Object.isFrozen(request.preopens)) {
    throw new TypeError("Preview 2 preopen grants must be an immutable array");
  }
  const paths = Object.create(null);
  const access = request.preopens.map(preopen => {
    assertExactDataObject(preopen, preopenKeys, "Preview 2 preopen grant");
    if (!Object.isFrozen(preopen) || typeof preopen.hostPath !== "string"
        || typeof preopen.guestPath !== "string"
        || preopen.access !== "readOnly" && preopen.access !== "readWrite"
        || Object.hasOwn(paths, preopen.guestPath)) {
      throw new TypeError("Preview 2 preopen grant is invalid");
    }
    paths[preopen.guestPath] = preopen.hostPath;
    return Object.freeze({ guestPath: preopen.guestPath, access: preopen.access });
  });
  const source = request.createFilesystem(Object.freeze({
    preopens: Object.freeze(paths),
  }));
  const filesystem = createPreview2Filesystem({
    filesystem: source,
    preopenAccess: Object.freeze(access),
  });
  let disposed = false;
  return Object.freeze({
    types: filesystem.types,
    preopens: filesystem.preopens,
    mountReadOnlyFile: filesystem.mountReadOnlyFile,
    dispose() {
      if (disposed) return;
      disposed = true;
      const errors = [];
      try { filesystem.dispose(); } catch (error) { errors.push(error); }
      try { source?.dispose?.(); } catch (error) { errors.push(error); }
      if (errors.length !== 0) {
        throw new AggregateError(errors, "Preview 2 filesystem scope cleanup failed");
      }
    },
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
