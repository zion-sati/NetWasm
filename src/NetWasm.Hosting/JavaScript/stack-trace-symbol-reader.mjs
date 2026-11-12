import { NetWasmHostError } from "./managed-errors.mjs";
import { readMethodSymbols } from "./symbol-sidecar-reader.mjs";

// A program's method-symbols sidecar (<program>.wasm.method-symbols) as the frames a host registers with the
// runtime; or symbols already read.
export function parseStackTraceSymbols(value) {
  if (value == null) {
    return [];
  }
  if (Array.isArray(value)) {
    return parseMethods(value);
  }
  const bytes = value instanceof ArrayBuffer ? new Uint8Array(value) : value;
  if (!(bytes instanceof Uint8Array)) {
    throw new NetWasmHostError("a NetWasm method-symbols sidecar must be bytes");
  }
  try {
    return parseMethods(readMethodSymbols(bytes));
  } catch (error) {
    throw new NetWasmHostError(`invalid NetWasm method-symbols sidecar: ${error.message}`);
  }
}

function parseMethods(methods) {
  const ids = new Set();
  const symbols = methods.map((method, index) => {
    if (method == null || typeof method !== "object"
        || !Number.isInteger(method.id) || method.id <= 0
        || method.id > 0xffffffff || typeof method.name !== "string"
        || method.name.length === 0) {
      throw new NetWasmHostError(
        `invalid NetWasm stack-trace symbol at index ${index}`);
    }
    if (ids.has(method.id)) {
      throw new NetWasmHostError(
        `duplicate NetWasm stack-trace method id ${method.id}`);
    }
    ids.add(method.id);
    return Object.freeze({ id: method.id, name: method.name });
  });
  return Object.freeze(symbols);
}
