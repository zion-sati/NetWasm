import { loadStackTraceSymbols } from "./stack-trace-symbol-loader.mjs";
import { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";
import {
  bindNetWasmInterop,
  prepareParsedNetWasmInterop,
} from "./raw-interop-preparation.mjs";

export {
  managedExceptionBrand,
  NetWasmHostError,
  NetWasmManagedError,
} from "./managed-errors.mjs";
export {
  loadStackTraceSymbols,
} from "./stack-trace-symbol-loader.mjs";
export { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";
export { registerStackTraceSymbols } from "./stack-trace-symbol-registrar.mjs";
export { createTargetAdapter } from "./target-adapter.mjs";
export { createInteropHandleTable as createHandleTable } from "./interop-handle-table.mjs";
export { createManagedExports } from "./managed-interop-export.mjs";
export { parseInteropManifest } from "./interop-manifest-reader.mjs";
export {
  bindNetWasmInterop,
  disposeNetWasmInterop,
  prepareNetWasmInterop,
  prepareRawNetWasmInterop,
} from "./raw-interop-preparation.mjs";

// Retained compatibility facade for direct runtime and diagnostic fixtures.
// Public application execution is owned by NetWasm.Hosting.executeNetWasm.
export async function instantiateNetWasm({
  module,
  manifest,
  runtimeModules,
  builtinServices = {},
  consumerModules = {},
  diagnosticArtifacts,
  managedExceptionReporting = {},
  stackTraceSymbols,
  stackTraceSymbolsUrl,
}) {
  if (stackTraceSymbols != null && stackTraceSymbolsUrl != null) {
    throw new NetWasmHostError(
      "provide stack-trace symbols or a sidecar URL, not both");
  }
  const parsedStackTraceSymbols = stackTraceSymbolsUrl == null
    ? parseStackTraceSymbols(stackTraceSymbols)
    : await loadStackTraceSymbols(stackTraceSymbolsUrl);
  const preparation = prepareParsedNetWasmInterop({
    manifest,
    runtimeModules,
    builtinServices,
    consumerModules,
    diagnosticArtifacts,
    managedExceptionReporting,
    parsedStackTraceSymbols,
  });
  const result = await WebAssembly.instantiate(module, preparation.imports);
  const instance = result instanceof WebAssembly.Instance ? result : result.instance;
  return bindNetWasmInterop({ preparation, instance });
}
