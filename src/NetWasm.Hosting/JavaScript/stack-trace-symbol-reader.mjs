import { NetWasmHostError } from "./managed-errors.mjs";

export function parseStackTraceSymbols(value) {
  if (value == null) {
    return [];
  }
  if (Array.isArray(value)) {
    return parseMethods(value);
  }
  let artifact;
  try {
    artifact = typeof value === "string" ? JSON.parse(value) : value;
  } catch (error) {
    throw new NetWasmHostError(
      `invalid NetWasm stack-trace symbol sidecar: ${error.message}`);
  }
  if (artifact == null || typeof artifact !== "object"
      || artifact.schemaVersion !== 1
      || !Array.isArray(artifact.methods)) {
    throw new NetWasmHostError(
      "invalid NetWasm stack-trace symbol sidecar schema");
  }
  return parseMethods(artifact.methods);
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
