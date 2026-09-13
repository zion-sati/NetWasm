// Test-only Adapter: lower explicitly selected environment pairs into guest memory.
// Capabilities: getMemory(), allocate(byteLength, alignment). No ambient environment.
export function createGuestEnvironmentImports(target, getMemory, allocate, environment) {
  if (target !== 'wasm32' && target !== 'wasm64')
    throw new TypeError('Unsupported environment target');
  if (typeof getMemory !== 'function' || typeof allocate !== 'function')
    throw new TypeError('Guest memory and allocation capabilities are required');
  if (environment === null || typeof environment !== 'object' || Array.isArray(environment))
    throw new TypeError('An explicit environment object is required');
  const entries = Object.entries(environment).map(([name, value]) => {
    if (typeof value !== 'string')
      throw new TypeError('Environment values must be strings');
    return [new TextEncoder().encode(name), new TextEncoder().encode(value)];
  });
  const width = target === 'wasm64' ? 8 : 4;
  const prefix = target === 'wasm64' ? 'cm64p2' : 'cm32p2';
  function writeAddress(address, value) {
    const view = new DataView(getMemory().buffer);
    if (width === 8) view.setBigUint64(Number(address), BigInt(value), true);
    else view.setUint32(Number(address), Number(value), true);
  }
  function lower(bytes) {
    if (bytes.length === 0) return 0;
    const address = Number(allocate(bytes.length, 1));
    new Uint8Array(getMemory().buffer, address, bytes.length).set(bytes);
    return address;
  }
  return {
    [`${prefix}|wasi:cli/environment@0.2`]: {
      'get-environment'(result) {
        const elements = entries.length === 0 ? 0 : Number(allocate(entries.length * width * 4, width));
        for (let index = 0; index < entries.length; index++) {
          for (let field = 0; field < 2; field++) {
            const bytes = entries[index][field];
            const address = lower(bytes);
            const slot = elements + (index * 4 + field * 2) * width;
            writeAddress(slot, address);
            writeAddress(slot + width, bytes.length);
          }
        }
        writeAddress(result, elements);
        writeAddress(Number(result) + width, entries.length);
      },
    },
  };
}
