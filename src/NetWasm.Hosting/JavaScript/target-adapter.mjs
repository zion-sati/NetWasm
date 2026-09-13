import { NetWasmHostError } from "./managed-errors.mjs";

export function createTargetAdapter(target, memory) {
  if (target !== "wasm32" && target !== "wasm64") {
    throw new NetWasmHostError("host adaptation target must be wasm32 or wasm64");
  }
  if (memory !== undefined && !(memory instanceof WebAssembly.Memory)) {
    throw new NetWasmHostError("a WebAssembly.Memory is required for host adaptation");
  }
  const maximumAddress = target === "wasm32"
    ? 0xffffffff
    : 0xffffffffffffffffn;
  return Object.freeze({
    target,
    memory,
    toAddress(value) {
      if (target === "wasm32") {
        if (!Number.isSafeInteger(value) || value < 0 || value > maximumAddress) {
          throw new NetWasmHostError(
            "wasm32 address is outside the JavaScript-safe range");
        }
        return value;
      }
      if (typeof value !== "bigint" || value < 0n || value > maximumAddress) {
        throw new NetWasmHostError(
          "wasm64 address must be an unsigned BigInt");
      }
      return value;
    },
  });
}
