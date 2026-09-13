import { NetWasmHostError } from "./managed-errors.mjs";
import { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";

export async function loadStackTraceSymbols(url, fetchImpl = globalThis.fetch) {
  if (typeof url !== "string" || url.length === 0) {
    throw new NetWasmHostError("stack-trace symbol sidecar URL is required");
  }
  if (typeof fetchImpl !== "function") {
    throw new NetWasmHostError(
      "stack-trace symbol sidecar loading requires fetch");
  }
  let response;
  try {
    response = await fetchImpl(url);
  } catch (error) {
    throw new NetWasmHostError(
      `failed to load stack-trace symbol sidecar '${url}': ${error.message}`);
  }
  if (!response.ok) {
    throw new NetWasmHostError(
      `failed to load stack-trace symbol sidecar '${url}': HTTP ${response.status}`);
  }
  return parseStackTraceSymbols(await response.text());
}
