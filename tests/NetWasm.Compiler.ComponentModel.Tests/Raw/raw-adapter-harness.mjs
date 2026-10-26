// Instantiates a generated raw adapter's canonical bindings against real
// linear memory, a recording bump reallocator and the host's resource store.
import { pathToFileURL } from "node:url";

export async function openAdapter({ adapterPath, repositoryRoot, target, providers, heapStart = 1024 }) {
  const hosting = `${repositoryRoot}/src/NetWasm.Hosting/JavaScript`;
  const store = await import(pathToFileURL(`${hosting}/raw-resource-store.mjs`).href);
  const releaser = await import(pathToFileURL(`${hosting}/raw-provider-resource-releaser.mjs`).href);
  const adapter = await import(pathToFileURL(adapterPath).href);
  const wasm64 = target === "wasm64";
  const memory = new WebAssembly.Memory({ initial: 1, maximum: 256 });
  const resources = store.createRawResourceStore();
  const reallocations = [];
  let next = heapStart;
  const reallocate = (address, oldSize, alignment, size) => {
    reallocations.push([Number(address), Number(oldSize), Number(alignment), Number(size)]);
    if (Number(size) === 0) return wasm64 ? 0n : 0;
    next = Math.ceil(next / Number(alignment)) * Number(alignment);
    const result = next;
    next += Number(size);
    while (next > memory.buffer.byteLength) memory.grow(1);
    return wasm64 ? BigInt(result) : result;
  };
  const canonical = Object.freeze({
    borrow: store.borrowRawResource,
    drop: store.dropRawResource,
    memory: () => memory,
    providers,
    reallocate,
    register: store.registerRawResource,
    release: releaser.releaseRawProviderResource,
    store: () => resources,
    transfer: store.transferRawResource,
  });
  const product = adapter.createAdapter({ metadata: adapter.rawAdapterMetadata, canonical });
  const module = Object.values(product.imports)[0];
  return {
    address: value => wasm64 ? BigInt(value) : value,
    imports: module,
    memory,
    reallocations,
    register: value => store.registerRawResource({ release: null, store: resources, type: 0, value }),
    borrow: (type, handle) => store.borrowRawResource({ handle, store: resources, type }),
    resources,
    view: () => new DataView(memory.buffer),
    wasm64,
    writeBytes(address, bytes) {
      new Uint8Array(memory.buffer, address, bytes.length).set(bytes);
    },
    readBytes(address, length) {
      return new Uint8Array(memory.buffer, address, length).slice();
    },
    readPointer(address) {
      const view = new DataView(memory.buffer);
      return wasm64 ? Number(view.getBigUint64(address, true)) : view.getUint32(address, true);
    },
  };
}
