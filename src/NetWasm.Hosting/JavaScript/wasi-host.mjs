import { createManagedExceptionReporter } from "./managed-exception-reporter.mjs";

export function createNetWasmWasiHost({
  wasiImports,
  stringDataOffset,
  diagnosticArtifacts = null,
  managedExceptionReporting = {},
  additionalImports = {},
}) {
  if (wasiImports === null || typeof wasiImports !== "object")
    throw new TypeError("wasiImports must be an object.");
  if (additionalImports === null || typeof additionalImports !== "object")
    throw new TypeError("additionalImports must be an object.");

  for (const [namespace, members] of Object.entries(wasiImports)) {
    if (!/^(?:cm(?:32|64)p2\|)?wasi:[^/]+\/[^@]+@0\.2(?:\.\d+)?$/.test(namespace))
      throw new TypeError("wasiImports must contain namespaced WASI Preview 2 interfaces.");
    if (members === null || typeof members !== "object")
      throw new TypeError("WASI interface members must be an object.");
  }
  for (const namespace of Object.keys(additionalImports)) {
    if (namespace === "wasi_snapshot_preview1" || namespace === "wasi_unstable")
      throw new TypeError("WASI Preview 1 imports are not supported.");
    if (namespace === "netwasm.host.v1" || Object.hasOwn(wasiImports, namespace))
      throw new TypeError("Import namespaces must not overwrite supplied host services.");
  }

  let activeMemory;
  const reporter = createManagedExceptionReporter({
    getMemory: () => activeMemory,
    stringDataOffset,
    maximumReportedMessageLength:
      managedExceptionReporting.maximumMessageLength ?? Number.POSITIVE_INFINITY,
    reportImmediate:
      managedExceptionReporting.reportImmediate ??
      (event => console.error(
        `[netwasm:${event.eventId}] managed exception typeId=${event.typeId}: ${event.message}`)),
    reportEnriched:
      managedExceptionReporting.reportEnriched ??
      (event => console.error(
        `[netwasm:${event.eventId}] resolved exception type: ${event.typeName}`)),
    loadArtifacts: async () => {
      if (typeof diagnosticArtifacts === "function")
        return diagnosticArtifacts();
      if (diagnosticArtifacts !== null)
        return diagnosticArtifacts;
      throw new Error("Managed exception diagnostic artifacts were not supplied.");
    },
  });

  const imports = Object.freeze({
    ...additionalImports,
    ...wasiImports,
    "netwasm.host.v1": reporter.importObject,
  });

  return Object.freeze({
    imports,
    bindInstance(instance) {
      const memory = instance?.exports?.memory;
      if (!(memory instanceof WebAssembly.Memory))
        throw new TypeError("The NetWasm instance must export its WebAssembly memory.");
      activeMemory = memory;
      return instance;
    },
    consumeTerminalEvent: reporter.consumeTerminalEvent,
  });
}
