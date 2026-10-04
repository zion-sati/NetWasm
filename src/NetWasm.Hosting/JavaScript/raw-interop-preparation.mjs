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
import { resolveInteropMemory } from "./interop-memory-resolver.mjs";
import { observeManagedAsyncExport } from "./managed-async-export-observer.mjs";
import { createManagedExceptionCapture } from "./managed-exception-capture.mjs";
import { createManagedExceptionPayloadConsumer } from "./managed-exception-payload-consumer.mjs";
import { createManagedExceptionTypeResolver } from "./managed-exception-type-resolver.mjs";

const builtinModule = "netwasm.host.v1";
const exceptionReporters = new WeakMap();

export function prepareNetWasmInterop({
  manifest,
  runtimeModules,
  builtinServices = {},
  consumerModules = {},
  diagnosticArtifacts,
  managedExceptionReporting = {},
  assertAsyncDeliveryAvailable = () => {},
  assertAvailable = () => {},
  observeAsyncFailure = () => {},
  observeAsyncCompletion = () => {},
  stackTraceSymbols,
}) {
  return prepareParsedNetWasmInterop({
    manifest,
    runtimeModules,
    builtinServices,
    consumerModules,
    diagnosticArtifacts,
    managedExceptionReporting,
    assertAsyncDeliveryAvailable,
    assertAvailable,
    observeAsyncFailure,
    observeAsyncCompletion,
    parsedStackTraceSymbols: parseStackTraceSymbols(stackTraceSymbols),
  });
}

export function prepareRawNetWasmInterop(options) {
  const preparation = prepareNetWasmInterop(options);
  const exceptionReporter = exceptionReporters.get(preparation);
  return Object.freeze({
    imports: preparation.imports,
    bindInstance(instance) {
      return bindInteropExecutionInstance({ preparation, instance });
    },
    close() {
      closeInteropExecution({ preparation });
    },
    consumeTerminalEvent() {
      return exceptionReporter.consumeTerminalEvent();
    },
    drainTerminalReports() {
      return exceptionReporter.drain();
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
  assertAsyncDeliveryAvailable = () => {},
  assertAvailable = () => {},
  observeAsyncFailure = () => {},
  observeAsyncCompletion = () => {},
  parsedStackTraceSymbols,
}) {
  if (typeof assertAsyncDeliveryAvailable !== "function"
      || typeof assertAvailable !== "function" || typeof observeAsyncFailure !== "function" || typeof observeAsyncCompletion !== "function") {
    throw new TypeError("managed interop lifetime actions are invalid");
  }
  const contract = parseInteropManifest(manifest);
  if (Object.hasOwn(consumerModules, builtinModule)) {
    throw new NetWasmHostError(
      "NetWasm built-in modules cannot be supplied by a consumer");
  }

  const handles = createInteropHandleTable();
  const pendingAsyncOperations = new Map();
  const pendingAsyncExports = new Set();
  let closed = false;
  let activeImports = 0;
  let deferred;
  let exceptionReporter;
  let consumeExceptionPayload;
  const preparation = prepareInteropExecution({
    createImports(readers) {
      deferred = readers;
      exceptionReporter = createManagedExceptionReporter({
        getMemory: readers.readMemory,
        stringDataOffset: contract.targetLayout.stringDataOffset,
        maximumReportedMessageLength:
          managedExceptionReporting.maximumMessageLength ?? Number.POSITIVE_INFINITY,
        reportImmediate: managedExceptionReporting.reportImmediate ?? (event =>
          console.error(`Managed exception event ${event.eventId}: type #${event.typeId}: ${event.message ?? "<no stored message>"}${event.messageTruncated ? " (message truncated by host policy)" : ""}; resolving type name${event.stackTrace ? `\n${event.stackTrace}` : ""}`)),
        reportEnriched: managedExceptionReporting.reportEnriched ?? (event =>
          console.error(`Managed exception event ${event.eventId}: ${event.typeName}${event.stackTrace ? `\n${event.stackTrace}` : ""}`)),
        loadArtifacts: loadDiagnosticArtifacts,
        stackTraceSymbols: parsedStackTraceSymbols,
      });
      const captureException = createManagedExceptionCapture({
        getMemory: readers.readMemory,
        stringDataOffset: contract.targetLayout.stringDataOffset,
        acquireHandle: handles.acquire,
        stackTraceSymbols: parsedStackTraceSymbols,
      });
      consumeExceptionPayload = createManagedExceptionPayloadConsumer({
        getHandle: handles.get,
        releaseHandle: handles.release,
        resolveType: createManagedExceptionTypeResolver({
          loadArtifacts: loadDiagnosticArtifacts,
          crypto: globalThis.crypto,
        }),
      });
      const builtins = createInteropBuiltinServiceModule({
        builtinServices: { ...builtinServices, capture_managed_exception_v1: captureException },
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
      return resolveInteropMemory({ instance, runtimeModules, target: contract.target });
    },
    bindInstance() {
      const instance = deferred.readInstance();
      const memory = deferred.readMemory();
      const targetAdapter = createTargetAdapter(contract.target, memory);
      const runtime = runtimeModules["netwasm.runtime.v1"];
      if (runtime != null) {
        registerStackTraceSymbols(
          runtime,
          memory,
          parsedStackTraceSymbols,
          targetAdapter);
      }
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
          consumeExceptionPayload,
          descriptor: managedExport,
          instance,
          exceptionReporter,
          getMemory: deferred.readMemory,
          handles,
          target: contract.target,
          targetLayout: contract.targetLayout,
          assertAvailable() {
            if (closed) throw new NetWasmHostError("NetWasm export session is closed");
            assertAvailable();
          },
          observeAsyncExport(request) {
            return observeManagedAsyncExport({
              ...request,
              registerAbort(abort) {
                pendingAsyncExports.add(abort);
                return () => { pendingAsyncExports.delete(abort); };
              },
            });
          },
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
      closed = true;
      const releaseActions = [
        ...[...pendingAsyncOperations.values()].map(operation => () => operation.cancel()),
        ...[...pendingAsyncExports].map(abort => () =>
          abort(new NetWasmHostError("NetWasm export session is closed"))),
        () => handles.dispose(),
      ];
      pendingAsyncOperations.clear();
      pendingAsyncExports.clear();
      const failures = [];
      for (const release of releaseActions) {
        try { release(); } catch (cause) { failures.push(cause); }
      }
      if (failures.length !== 0) {
        throw new AggregateError(failures, "NetWasm interop cleanup failed");
      }
    },
  });
  exceptionReporters.set(preparation, exceptionReporter);
  return preparation;

  async function loadDiagnosticArtifacts() {
    if (typeof diagnosticArtifacts === "function") return diagnosticArtifacts();
    if (diagnosticArtifacts != null) return diagnosticArtifacts;
    throw new Error("diagnostic exception artifacts were not deployed");
  }

  function createStatusService(descriptor, service, readers) {
    const invoke = createInteropStatusService({
      assertAsyncDeliveryAvailable,
      callbacks: contract.callbacks ?? [],
      descriptor,
      exceptionReporter,
      getInstance: readers.readInstance,
      getMemory: readers.readMemory,
      handles,
      pendingAsyncOperations,
      observeAsyncFailure,
      service,
      statusAbi: contract.statusAbi,
      target: contract.target,
      targetLayout: contract.targetLayout,
    }, error => { if (!closed) observeAsyncCompletion(error); }, () => activeImports !== 0);
    return Object.freeze((...arguments_) => {
      activeImports++;
      try { return invoke(...arguments_); }
      finally { activeImports--; }
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
