import { NetWasmHostError } from "./managed-errors.mjs";

const memoryExports = Object.freeze({
  wasm32: "cm32p2_memory",
  wasm64: "cm64p2_memory",
});

export function resolveInteropMemory({ instance, runtimeModules, target }) {
  const name = memoryExports[target];
  if (name === undefined) {
    throw new NetWasmHostError("NetWasm interop memory target is unsupported");
  }
  let memory;
  for (const candidate of [
    instance.exports[name],
    instance.exports.memory,
    runtimeModules["netwasm.runtime.v1"]?.memory,
  ]) {
    if (candidate == null) continue;
    if (!(candidate instanceof WebAssembly.Memory)) {
      throw new NetWasmHostError("NetWasm interop memory alias is invalid");
    }
    if (memory !== undefined && memory !== candidate) {
      throw new NetWasmHostError("NetWasm interop memory aliases disagree");
    }
    memory = candidate;
  }
  if (memory === undefined) {
    throw new NetWasmHostError("instantiated NetWasm module did not expose memory");
  }
  return memory;
}
