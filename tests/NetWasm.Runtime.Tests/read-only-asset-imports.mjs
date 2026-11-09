// Test-only Adapter: expose one explicitly selected immutable file through WASI 0.3's
// filesystem, as CoreLib's time zone reader calls it: open-at is async-lowered with its six
// flat parameters in memory, and a file reads through a stream<u8> and a completion future.
// Every call completes at once, so nothing ever waits.
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
  // A subtask that returned, and a stream or future copy that completed or found its other
  // end dropped.
  const returned = 2;
  const completed = 0;
  const dropped = 1;
  // Ordinals of wasi:filesystem@0.3.2's error-code variant.
  const badDescriptor = 2;
  const isDirectory = 13;
  const noEntry = 19;
  const notDirectory = 23;
  const unsupported = 26;
  const notPermitted = 30;
  const readOnly = 32;
  const descriptors = new Map();
  const streams = new Map();
  const futures = new Map();
  let nextHandle = 1;
  const view = () => new DataView(getMemory().buffer);
  const writeAddress = (pointer, value) => {
    if (width === 8) view().setBigUint64(Number(pointer), BigInt(value), true);
    else view().setUint32(Number(pointer), Number(value), true);
  };
  const lower = data => {
    const pointer = Number(allocate(data.length, 1));
    new Uint8Array(getMemory().buffer, pointer, data.length).set(data);
    return pointer;
  };
  // A result's error-code lies at offset 4, after its discriminant.
  const fail = (result, code) => {
    view().setUint8(Number(result) + 4, code);
    view().setUint8(Number(result), 1);
  };
  // The parameters record CoreLib writes: descriptor, path-flags, path (address, length),
  // open-flags, descriptor-flags.
  const open = (parameters, result) => {
    const record = Number(parameters);
    const directory = view().getInt32(record, true);
    const pathFlags = view().getUint8(record + 4);
    const path = view().getUint32(record + 8, true);
    const pathLength = view().getUint32(record + 12, true);
    const openFlags = view().getUint8(record + 16);
    const flags = view().getUint8(record + 17);
    const kind = descriptors.get(directory);
    if (kind === undefined) fail(result, badDescriptor);
    else if (kind !== 'directory') fail(result, notDirectory);
    else if ((openFlags & 9) !== 0 || (flags & 34) !== 0) fail(result, readOnly);
    else if ((openFlags & 2) !== 0) fail(result, notDirectory);
    else if ((pathFlags !== 0 && pathFlags !== 1) || openFlags !== 0 || flags !== 1)
      fail(result, unsupported);
    else {
      const name = new TextDecoder('utf-8', { fatal: true }).decode(
        new Uint8Array(getMemory().buffer, path, pathLength));
      if (name.startsWith('/') || name.split('/').includes('..')) fail(result, notPermitted);
      else if (name !== fileName) fail(result, noEntry);
      else {
        const handle = nextHandle++;
        descriptors.set(handle, 'file');
        view().setInt32(Number(result) + 4, handle, true);
        view().setUint8(Number(result), 0);
      }
    }
    return returned;
  };
  // A descriptor that is no file ends its stream at once and fails its future.
  const readViaStream = (handle, offset, result) => {
    const kind = descriptors.get(handle);
    const error = kind === undefined ? badDescriptor : kind !== 'file' ? isDirectory : null;
    const start = BigInt.asUintN(64, BigInt(offset));
    const stream = nextHandle++;
    const future = nextHandle++;
    streams.set(stream, error !== null || start >= BigInt(contents.length) ? contents.length : Number(start));
    futures.set(future, error);
    view().setInt32(Number(result), stream, true);
    view().setInt32(Number(result) + 4, future, true);
  };
  const readStream = (stream, pointer, length) => {
    const position = streams.get(stream);
    if (position === undefined) throw new TypeError('Unknown asset stream');
    if (position === contents.length) return dropped;
    const count = Math.min(Number(length), contents.length - position);
    new Uint8Array(getMemory().buffer, Number(pointer), count)
      .set(contents.subarray(position, position + count));
    streams.set(stream, position + count);
    return count * 16 + completed;
  };
  const readFuture = (future, result) => {
    if (!futures.has(future)) throw new TypeError('Unknown asset future');
    const error = futures.get(future);
    if (error === null) view().setUint8(Number(result), 0);
    else fail(result, error);
    return completed;
  };
  const release = (table, label) => handle => {
    if (!table.delete(handle)) throw new TypeError(`Unknown asset ${label}`);
  };
  return {
    [`${prefix}|wasi:filesystem/preopens@0.3`]: {
      'get-directories'(result) {
        const path = lower(pathBytes);
        const elements = Number(allocate(width * 3, width));
        const handle = nextHandle++;
        view().setInt32(elements, handle, true);
        writeAddress(elements + width, path);
        writeAddress(elements + width * 2, pathBytes.length);
        writeAddress(result, elements);
        writeAddress(Number(result) + width, 1);
        descriptors.set(handle, 'directory');
      },
    },
    [`${prefix}|wasi:filesystem/types@0.3`]: {
      '[method]descriptor.read-via-stream': readViaStream,
      '[resource-drop]descriptor': release(descriptors, 'descriptor'),
    },
    'wasi:filesystem/types@0.3.2': {
      '[async-lower][method]descriptor.open-at': open,
      '[async-lower][stream-read-0][method]descriptor.read-via-stream': readStream,
      '[stream-drop-readable-0][method]descriptor.read-via-stream': release(streams, 'stream'),
      '[async-lower][future-read-1][method]descriptor.read-via-stream': readFuture,
      '[future-drop-readable-1][method]descriptor.read-via-stream': release(futures, 'future'),
    },
  };
}
