import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { createInteropBuiltinServiceModule } from "./interop-builtin-service-module.mjs";
import { createInteropStatusService } from "./interop-status-service.mjs";
import {
  bindInteropExecutionInstance,
  closeInteropExecution,
  prepareInteropExecution,
} from "./interop-execution-preparation.mjs";
import { parseInteropManifest } from "./interop-manifest-reader.mjs";
import {
  createManagedInteropExport,
} from "./managed-interop-export.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";
import { createManagedExceptionReporter } from "./managed-exception-reporter.mjs";
import { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";
import { registerStackTraceSymbols } from "./stack-trace-symbol-registrar.mjs";
import { createTargetAdapter } from "./target-adapter.mjs";

const builtinModule = "netwasm.host.v1";

export function prepareNetWasmInterop({
  manifest,
  runtimeModules,
  builtinServices = {},
  consumerModules = {},
  diagnosticArtifacts,
  managedExceptionReporting = {},
  stackTraceSymbols,
}) {
  return prepareParsedNetWasmInterop({
    manifest,
    runtimeModules,
    builtinServices,
    consumerModules,
    diagnosticArtifacts,
    managedExceptionReporting,
    parsedStackTraceSymbols: parseStackTraceSymbols(stackTraceSymbols),
  });
}

export function prepareRawNetWasmInterop(options) {
  const preparation = prepareNetWasmInterop(options);
  return Object.freeze({
    imports: preparation.imports,
    bindInstance(instance) {
      return bindInteropExecutionInstance({ preparation, instance });
    },
    close() {
      closeInteropExecution({ preparation });
    },
  });
}

export function prepareParsedNetWasmInterop({
  manifest,
  runtimeModules,
  builtinServices = {},
  consumerModules = {},
  diagnosticArtifacts,
  managedExceptionReporting = {},
  parsedStackTraceSymbols,
}) {
  const contract = parseInteropManifest(manifest);
  if (Object.hasOwn(consumerModules, builtinModule)) {
    throw new NetWasmHostError(
      "NetWasm built-in modules cannot be supplied by a consumer");
  }

  const handles = createInteropHandleTable();
  const pendingAsyncOperations = new Map();
  let deferred;
  let exceptionReporter;
  return prepareInteropExecution({
    createImports(readers) {
      deferred = readers;
      exceptionReporter = createManagedExceptionReporter({
        getMemory: readers.readMemory,
        stringDataOffset: contract.targetLayout.stringDataOffset,
        maximumReportedMessageLength:
          managedExceptionReporting.maximumMessageLength ?? Number.POSITIVE_INFINITY,
        reportImmediate: managedExceptionReporting.reportImmediate ?? (event =>
          console.error(`Managed exception event ${event.eventId}: type #${event.typeId}: ${event.message ?? "<no stored message>"}${event.messageTruncated ? " (message truncated by host policy)" : ""}; resolving type name`)),
        reportEnriched: managedExceptionReporting.reportEnriched ?? (event =>
          console.error(`Managed exception event ${event.eventId}: ${event.typeName}`)),
        loadArtifacts: async () => {
          if (typeof diagnosticArtifacts === "function") return diagnosticArtifacts();
          if (diagnosticArtifacts != null) return diagnosticArtifacts;
          throw new Error("diagnostic exception artifacts were not deployed");
        },
      });
      const builtins = createInteropBuiltinServiceModule({
        builtinServices,
        contract,
        createStatusService: (descriptor, service) => createStatusService(
          descriptor, service, readers),
        exceptionReporter,
        handles,
        readMemory: readers.readMemory,
      });
      const imports = { ...runtimeModules, [builtinModule]: builtins };
      for (const descriptor of contract.imports) {
        if (descriptor.module === builtinModule) {
          continue;
        }
        const serviceModule = consumerModules[descriptor.module];
        const service = serviceModule?.[descriptor.name];
        if (typeof service !== "function") {
          throw new NetWasmHostError(
            `missing host service ${descriptor.module}.${descriptor.name}`);
        }
        imports[descriptor.module] ??= {};
        imports[descriptor.module][descriptor.name] = createStatusService(
          descriptor, service, readers);
      }
      return imports;
    },
    resolveMemory(instance) {
      const memory = instance.exports.memory ?? runtimeModules["netwasm.runtime.v1"]?.memory;
      if (!(memory instanceof WebAssembly.Memory)) {
        throw new NetWasmHostError(
          "instantiated NetWasm module did not expose memory");
      }
      return memory;
    },
    bindInstance() {
      const instance = deferred.readInstance();
      const memory = deferred.readMemory();
      const targetAdapter = createTargetAdapter(contract.target, memory);
      registerStackTraceSymbols(
        runtimeModules["netwasm.runtime.v1"],
        memory,
        parsedStackTraceSymbols,
        targetAdapter);
      const managedExports = {};
      for (const managedExport of contract.exports) {
        if (typeof instance.exports[managedExport.name] !== "function") {
          throw new NetWasmHostError(
            `managed export ${managedExport.name} is unavailable`);
        }
        if (managedExport.asyncReturn != null) {
          for (const helper of [managedExport.statusExport, managedExport.completeExport,
            managedExport.resultExport].filter(Boolean)) {
            if (typeof instance.exports[helper] !== "function") {
              throw new NetWasmHostError(
                `managed async export helper ${helper} is unavailable`);
            }
          }
        }
        managedExports[managedExport.name] = createManagedInteropExport({
          descriptor: managedExport,
          instance,
          exceptionReporter,
        });
      }
      for (const callback of contract.callbacks ?? []) {
        if (typeof instance.exports[callback.exportName] !== "function") {
          throw new NetWasmHostError(
            `managed callback ${callback.exportName} is unavailable`);
        }
      }
      for (const asyncImport of contract.imports.filter(
        descriptor => descriptor.asyncReturn != null)) {
        for (const helper of [asyncImport.resolveExport, asyncImport.rejectExport,
          asyncImport.cancelExport]) {
          if (typeof instance.exports[helper] !== "function") {
            throw new NetWasmHostError(
              `managed async import helper ${helper} is unavailable`);
          }
        }
      }
      return { instance, exports: managedExports, handles, adapter: targetAdapter };
    },
    close() {
      for (const operation of pendingAsyncOperations.values()) operation.cancel();
      pendingAsyncOperations.clear();
      handles.dispose();
    },
  });

  function createStatusService(descriptor, service, readers) {
    return createInteropStatusService({
      callbacks: contract.callbacks ?? [],
      descriptor,
      exceptionReporter,
      getInstance: readers.readInstance,
      getMemory: readers.readMemory,
      handles,
      pendingAsyncOperations,
      service,
      statusAbi: contract.statusAbi,
      target: contract.target,
      targetLayout: contract.targetLayout,
    });
  }
}

export function bindNetWasmInterop({ preparation, instance }) {
  const boundary = bindInteropExecutionInstance({ preparation, instance });
  return {
    ...boundary,
    dispose() { closeInteropExecution({ preparation }); },
  };
}

export function disposeNetWasmInterop({ preparation }) {
  closeInteropExecution({ preparation });
}
