import {
  bindCanonicalComponent,
  commandComponentContract,
} from "./canonical-component-binder.mjs";
import { executeCommand } from "./command-executor.mjs";
import {
  componentDiagnosticsModule,
  createComponentManagedExceptionHost,
} from "./component-managed-exception-host.mjs";
import { prepareComponentExecution } from "./component-execution-preparation.mjs";
import { createDiagnosticCommandExport } from "./diagnostic-command-export.mjs";
import { failedExecutionResult } from "./execution-result.mjs";
import { createTerminalManagedExport } from "./terminal-managed-export.mjs";

const adapterKeys = ["contractKey", "instantiate"];

// Runs a wasi:cli command component: instantiate the generated adapter over the
// projected imports, then await its run export, a callback-lifted task the host drives.
export async function executeComponent(request = {}) {
  if (request === null || typeof request !== "object" || Array.isArray(request)) {
    throw new TypeError("component execution request is required");
  }
  const {
    contractKey,
    adapter,
    imports,
    loadCoreModule,
    instantiateCore,
    diagnosticArtifacts,
    stackTraceSymbols = [],
    managedExceptionReporting = {},
    signal = null,
    releaseActions = [],
  } = request;
  validateAdapter(adapter);
  if (typeof loadCoreModule !== "function") {
    throw new TypeError("component core-module loader is required");
  }
  const prepared = prepareComponentExecution({
    contractKey,
    imports,
    instantiateCore,
    signal,
    releaseActions,
  });
  const diagnostics = createComponentManagedExceptionHost({
    diagnosticArtifacts,
    managedExceptionReporting,
    stackTraceSymbols,
  });
  const projectedImports = Object.freeze(Object.assign(
    Object.create(null),
    prepared.imports,
    { [componentDiagnosticsModule]: diagnostics.imports }));
  const closeScope = prepared.closeCallerScope;

  if (adapter.contractKey !== contractKey) {
    return closeScope(failedExecutionResult(
      "contractFailure",
      "instantiation",
      "contract.component-adapter",
      "The generated component adapter does not match the execution contract."));
  }
  if (signal?.aborted) {
    return closeScope(failedExecutionResult(
      "callerCancellation",
      "instantiation",
      "caller.cancelled",
      "Component execution was cancelled by the caller before instantiation."));
  }

  let instance;
  try {
    instance = await adapter.instantiate(Object.freeze({
      loadCoreModule,
      imports: projectedImports,
      instantiateCore: prepared.instantiateCore,
    }));
  } catch (error) {
    return closeScope(failedExecutionResult(
      "hostFailure",
      "instantiation",
      "host.component-instantiate",
      "The execution host could not instantiate the component.",
      error));
  }

  let binding;
  try {
    binding = bindCanonicalComponent({ contractKey: commandComponentContract, instance });
  } catch (error) {
    return closeScope(failedExecutionResult(
      "contractFailure",
      "instantiation",
      "contract.component-exports",
      "The generated component adapter returned an invalid canonical boundary.",
      error));
  }
  const run = binding.diagnosticCommand === undefined
    ? createTerminalManagedExport(
      "run",
      binding.command.run,
      diagnostics.consumeTerminalEvent,
      () => {})
    : createDiagnosticCommandExport(binding.diagnosticCommand.run, diagnostics.imports.report);
  const outcome = await executeCommand({ run, signal: prepared.signal });
  await diagnostics.drain();
  return closeScope(outcome);
}

function validateAdapter(adapter) {
  if (adapter === null || typeof adapter !== "object" || Array.isArray(adapter)) {
    throw new TypeError("generated component adapter is required");
  }
  const keys = Object.keys(adapter).sort();
  if (keys.length !== adapterKeys.length
      || keys.some((key, index) => key !== adapterKeys[index])) {
    throw new TypeError("generated component adapter shape is invalid");
  }
  if (typeof adapter.contractKey !== "string" || adapter.contractKey.length === 0
      || typeof adapter.instantiate !== "function") {
    throw new TypeError("generated component adapter is incomplete");
  }
}
