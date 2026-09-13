import { NetWasmHostError } from "./managed-errors.mjs";

export function registerStackTraceSymbols(runtime, memory, symbols, adapter) {
  if (!Array.isArray(symbols)) {
    throw new NetWasmHostError("stack-trace symbols must be an array");
  }
  if (symbols.length === 0) {
    return;
  }
  if (runtime == null || typeof runtime.native_alloc !== "function"
      || typeof runtime.native_free !== "function"
      || typeof runtime.stack_trace_register_symbol !== "function") {
    throw new NetWasmHostError(
      "runtime does not support stack-trace symbol registration");
  }
  if (!(memory instanceof WebAssembly.Memory)) {
    throw new NetWasmHostError(
      "stack-trace symbol registration requires runtime memory");
  }
  for (const symbol of symbols) {
    const byteLength = symbol.name.length * 2;
    const allocationSize = adapter.target === "wasm64"
      ? BigInt(byteLength)
      : byteLength;
    const address = runtime.native_alloc(allocationSize);
    if (address === 0 || address === 0n) {
      throw new NetWasmHostError(
        `failed to allocate stack-trace symbol ${symbol.id}`);
    }
    try {
      const offset = typeof address === "bigint" ? Number(address) : address;
      if (!Number.isSafeInteger(offset) || offset < 0
          || offset > memory.buffer.byteLength - byteLength) {
        throw new NetWasmHostError(
          `stack-trace symbol ${symbol.id} has an invalid memory address`);
      }
      const characters = new Uint16Array(
        memory.buffer,
        offset,
        symbol.name.length);
      for (let index = 0; index < symbol.name.length; index++) {
        characters[index] = symbol.name.charCodeAt(index);
      }
      runtime.stack_trace_register_symbol(
        symbol.id,
        address,
        symbol.name.length);
    } finally {
      runtime.native_free(address);
    }
  }
}
