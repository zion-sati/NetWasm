// Test-only Adapter: expose one explicitly selected immutable file through WASI.
// Injected capabilities: getMemory(), allocate(byteLength, alignment).
export function createReadOnlyAssetImports(target, getMemory, allocate, { mountPath, fileName, bytes }) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported asset target');
  if (typeof getMemory !== 'function' || typeof allocate !== 'function')
    throw new TypeError('Guest memory and allocation capabilities are required');
  if (typeof mountPath !== 'string' || !mountPath.startsWith('/') ||
      typeof fileName !== 'string' || fileName.length === 0 || fileName.includes('/') ||
      fileName === '.' || fileName === '..' || !(bytes instanceof Uint8Array))
    throw new TypeError('An explicit mount, relative file name and byte array are required');
  const contents = new Uint8Array(bytes);
  const pathBytes = new TextEncoder().encode(mountPath);
  const width = target === 'wasm64' ? 8 : 4;
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  const descriptors = new Map();
  let nextDescriptor = 1;
  const view = () => new DataView(getMemory().buffer);
  const writeAddress = (pointer, value) => {
    if (width === 8) view().setBigUint64(Number(pointer), BigInt(value), true);
    else view().setUint32(Number(pointer), Number(value), true);
  };
  const lower = data => {
    if (data.length === 0) return 0;
    const pointer = Number(allocate(data.length, 1));
    new Uint8Array(getMemory().buffer, pointer, data.length).set(data);
    return pointer;
  };
  // Enum ordinals from wit/wasi-0.2.11/filesystem/types.wit, not POSIX errno.
  const fail = (result, payloadOffset, code) => {
    view().setUint8(Number(result) + payloadOffset, code);
    view().setUint8(Number(result), 1);
  };
  const open = (directory, pathFlags, path, pathLength, openFlags, flags, result) => {
    const kind = descriptors.get(directory);
    if (kind === undefined) return fail(result, 4, 3); // bad-descriptor
    if (kind !== 'directory') return fail(result, 4, 24); // not-directory
    if ((openFlags & 9) !== 0 || (flags & 34) !== 0)
      return fail(result, 4, 33); // read-only
    if ((openFlags & 2) !== 0) return fail(result, 4, 24);
    if ((pathFlags !== 0 && pathFlags !== 1) || openFlags !== 0 || flags !== 1)
      return fail(result, 4, 27); // unsupported by this read-only test host
    const name = new TextDecoder('utf-8', { fatal: true }).decode(
      new Uint8Array(getMemory().buffer, Number(path), Number(pathLength)));
    if (name.startsWith('/') || name.split('/').includes('..'))
      return fail(result, 4, 31); // not-permitted
    if (name !== fileName) return fail(result, 4, 20); // no-entry
    const handle = nextDescriptor++;
    view().setInt32(Number(result) + 4, handle, true);
    view().setUint8(Number(result), 0);
    descriptors.set(handle, 'file');
  };
  const read = (handle, length, offset, result) => {
    const kind = descriptors.get(handle);
    if (kind === undefined) return fail(result, width, 3);
    if (kind !== 'file') return fail(result, width, 14); // is-directory
    const start = BigInt.asUintN(64, offset);
    const requested = BigInt.asUintN(64, length);
    const remaining = start >= BigInt(contents.length) ? 0n : BigInt(contents.length) - start;
    const count = Number(requested < remaining ? requested : remaining);
    const chunk = count === 0 ? new Uint8Array() : contents.subarray(Number(start), Number(start) + count);
    const pointer = lower(chunk);
    writeAddress(Number(result) + width, pointer);
    writeAddress(Number(result) + width * 2, count);
    view().setUint8(Number(result) + width * 3, start + BigInt(count) >= BigInt(contents.length) ? 1 : 0);
    view().setUint8(Number(result), 0);
  };
  const drop = handle => {
    if (!descriptors.delete(handle)) throw new TypeError('Unknown asset descriptor');
  };
  return {
    [`${prefix}|wasi:filesystem/preopens@0.2`]: {
      'get-directories'(result) {
        const path = lower(pathBytes);
        const elements = Number(allocate(width * 3, width));
        const handle = nextDescriptor++;
        view().setInt32(elements, handle, true);
        writeAddress(elements + width, path);
        writeAddress(elements + width * 2, pathBytes.length);
        writeAddress(result, elements);
        writeAddress(Number(result) + width, 1);
        descriptors.set(handle, 'directory');
      },
    },
    [`${prefix}|wasi:filesystem/types@0.2`]: {
      '[method]descriptor.open-at': open,
      '[method]descriptor.read': read,
      '[resource-drop]descriptor': drop,
      descriptor_open_at: open,
      descriptor_read: read,
      descriptor_drop: drop,
    },
  };
}
