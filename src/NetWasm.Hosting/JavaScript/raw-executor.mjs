import { commandExecutionContract } from "./execution-contracts.mjs";
import { executeCommand } from "./command-executor.mjs";
import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";
import { failedExecutionResult } from "./execution-result.mjs";
import { buildRawAbiPlan } from "./raw-abi-plan-builder.mjs";
import {
  bindRawCanonicalImportInstance,
  closeRawCanonicalImportBinding,
  createRawCanonicalImportBinding,
  readRawComponentModel,
} from "./raw-canonical-import-binding.mjs";
import { bindRawExecutionInstance } from "./raw-execution-instance-binder.mjs";
import { composeRawImports } from "./raw-import-composer.mjs";
import { createTerminalManagedExport } from "./terminal-managed-export.mjs";

// The module the Component Model's own built-ins come from, such as waitable sets.
const componentModelRootModule = "$root";

export async function executeRaw(request = {}) {
  if (request === null || typeof request !== "object" || Array.isArray(request)) {
    throw new TypeError("raw execution request is required");
  }
  const {
    contractKey,
    abi,
    adapter,
    module,
    providers,
    physicalProviders,
    instantiate,
    bindInstance = () => {},
    consumeTerminalEvent = () => undefined,
    drainTerminalReports = async () => {},
    subscribeInteropCompletion = () => () => {},
    signal = null,
    instanceReleaseActions = [],
    releaseActions = [],
  } = request;
  if (contractKey !== commandExecutionContract) {
    throw new TypeError("raw execution contract is unsupported");
  }
  if (module === null || typeof module !== "object") {
    throw new TypeError("verified raw module is required");
  }
  if (abi === null || typeof abi !== "object" || Array.isArray(abi)) {
    throw new TypeError("raw execution ABI is required");
  }
  if (adapter === null || typeof adapter !== "object" || Array.isArray(adapter)) {
    throw new TypeError("generated raw adapter is required");
  }
  if (providers === null || typeof providers !== "object" || Array.isArray(providers)) {
    throw new TypeError("raw logical providers are required");
  }
  if (physicalProviders === null || typeof physicalProviders !== "object"
      || Array.isArray(physicalProviders)) {
    throw new TypeError("raw physical providers are required");
  }
  if (typeof instantiate !== "function") {
    throw new TypeError("raw module instantiator is required");
  }
  if (typeof bindInstance !== "function") {
    throw new TypeError("raw instance binding action is required");
  }
  if (typeof consumeTerminalEvent !== "function"
      || typeof drainTerminalReports !== "function") {
    throw new TypeError("raw terminal exception actions are required");
  }
  validateSignal(signal);
  if (typeof subscribeInteropCompletion !== "function") {
    throw new TypeError("raw interop completion subscription is required");
  }
  const closeInstanceScope = createExecutionScopeCloser(instanceReleaseActions);
  const closeCallerScope = createExecutionScopeCloser(releaseActions);

  return executeRawCommand({
    contractKey,
    abi,
    adapter,
    module,
    providers,
    physicalProviders,
    instantiate,
    bindInstance,
    consumeTerminalEvent,
    drainTerminalReports,
    subscribeInteropCompletion,
    signal,
    closeInstanceScope,
    closeCallerScope,
  });
}

async function executeRawCommand({
  contractKey,
  abi,
  adapter,
  module,
  providers,
  physicalProviders,
  instantiate,
  bindInstance,
  consumeTerminalEvent,
  drainTerminalReports,
  subscribeInteropCompletion,
  signal,
  closeInstanceScope,
  closeCallerScope,
}) {
  if (Object.hasOwn(physicalProviders, componentModelRootModule)) {
    return closeOwnedScopes(
      rawAbiFailure(new TypeError("the Component Model built-ins are reserved by the executor")),
      closeInstanceScope,
      closeCallerScope);
  }
  let plan;
  try {
    plan = buildRawAbiPlan({ contractKey, abi });
  } catch (error) {
    return closeOwnedScopes(rawAbiFailure(error), closeInstanceScope, closeCallerScope);
  }
  let canonicalBinding;
  let componentModel;
  try {
    canonicalBinding = createRawCanonicalImportBinding({ adapter, providers });
    componentModel = readRawComponentModel({ binding: canonicalBinding });
  } catch (error) {
    return closeOwnedScopes(rawAbiFailure(error), closeInstanceScope, closeCallerScope);
  }
  const closeCanonicalScope = createExecutionScopeCloser([
    canonicalReleaseAction(canonicalBinding),
  ]);
  const closeScope = outcome => closeOwnedScopes(
    outcome,
    closeInstanceScope,
    closeCanonicalScope,
    closeCallerScope);
  let imports;
  try {
    imports = composeRawImports({
      canonicalBinding,
      inventory: importInventory(plan),
      physicalProviders: Object.freeze({
        ...physicalProviders,
        [componentModelRootModule]: componentModel.root,
      }),
    });
  } catch (error) {
    return closeScope(rawAbiFailure(error));
  }
  if (signal?.aborted) {
    return closeScope(preInstantiationCancellation());
  }
  let instance;
  try {
    instance = await instantiate(Object.freeze({ module, imports }));
  } catch (error) {
    return closeScope(rawInstantiationFailure(error));
  }
  try {
    bindInstance(instance);
  } catch (error) {
    return closeScope(rawInstanceBindingFailure(error));
  }
  let binding;
  try {
    bindRawCanonicalImportInstance({ binding: canonicalBinding, instance });
    binding = bindRawExecutionInstance({ componentModel, instance, plan });
  } catch (error) {
    return closeScope(rawExportFailure(error));
  }
  const run = createTerminalManagedExport(
    "run",
    binding.command.run,
    consumeTerminalEvent,
    () => {
      const clearActiveException = instance.exports.exception_clear_active;
      if (typeof clearActiveException !== "function") {
        throw new TypeError("raw managed exception cleanup export is unavailable");
      }
      clearActiveException.call(instance.exports);
    });
  // A JavaScript interop completion gives the guest's event loop a turn to run what it
  // queued.
  const unsubscribe = subscribeInteropCompletion(() => componentModel.wake());
  let outcome;
  try {
    outcome = await executeCommand({ run, signal });
  } finally {
    unsubscribe();
  }
  await drainTerminalReports();
  return closeScope(outcome);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("raw signal must be an AbortSignal");
  }
}

function importInventory(plan) {
  return Object.freeze({
    target: plan.target,
    imports: plan.imports,
  });
}

function rawAbiFailure(cause) {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.raw-abi",
    "The final raw module does not match its execution ABI, generated adapter, or providers.",
    cause);
}

function canonicalReleaseAction(binding) {
  return Object.freeze({
    code: "host.raw-canonical-imports",
    message: "The execution host could not close the raw canonical imports.",
    release() { closeRawCanonicalImportBinding({ binding }); },
  });
}

function rawInstantiationFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-instantiate",
    "The execution host could not instantiate the raw module.",
    cause);
}

function rawInstanceBindingFailure(cause) {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-instance-bind",
    "The execution host could not bind services to the raw instance.",
    cause);
}

function rawExportFailure(cause) {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.raw-exports",
    "The instantiated raw module does not expose its declared execution boundary.",
    cause);
}

function preInstantiationCancellation() {
  return failedExecutionResult(
    "callerCancellation",
    "instantiation",
    "caller.cancelled",
    "Raw execution was cancelled by the caller before instantiation.");
}

async function closeOwnedScopes(outcome, ...closers) {
  for (const close of closers) outcome = await close(outcome);
  return outcome;
}
