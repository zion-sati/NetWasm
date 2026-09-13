import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";
import { executeCommand } from "./command-executor.mjs";
import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";
import { failedExecutionResult } from "./execution-result.mjs";
import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";
import { observeManagedProcess } from "./managed-process-observer.mjs";
import { createPollableReactor } from "./pollable-reactor.mjs";
import { buildRawAbiPlan } from "./raw-abi-plan-builder.mjs";
import {
  bindRawCanonicalImportInstance,
  closeRawCanonicalImportBinding,
  createRawCanonicalImportBinding,
} from "./raw-canonical-import-binding.mjs";
import { bindRawExecutionInstance } from "./raw-execution-instance-binder.mjs";
import { composeRawImports } from "./raw-import-composer.mjs";

const opaqueReactorFailure = Object.freeze({});

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
    signal = null,
    instanceReleaseActions = [],
    releaseActions = [],
    schedule = globalThis.queueMicrotask?.bind(globalThis),
  } = request;
  if (contractKey !== commandExecutionContract && contractKey !== processExecutionContract) {
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
  validateSignal(signal);
  if (typeof schedule !== "function") {
    throw new TypeError("raw execution scheduler is required");
  }
  const closeInstanceScope = createExecutionScopeCloser(instanceReleaseActions);
  const closeCallerScope = createExecutionScopeCloser(releaseActions);

  if (contractKey === commandExecutionContract) {
    return executeRawCommand({
      contractKey,
      abi,
      adapter,
      module,
      providers,
      physicalProviders,
      instantiate,
      bindInstance,
      signal,
      closeInstanceScope,
      closeCallerScope,
    });
  }
  return executeRawProcess({
    contractKey,
    abi,
    adapter,
    module,
    providers,
    physicalProviders,
    instantiate,
    bindInstance,
    signal,
    closeInstanceScope,
    closeCallerScope,
    schedule,
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
  signal,
  closeInstanceScope,
  closeCallerScope,
}) {
  let plan;
  try {
    plan = buildRawAbiPlan({ contractKey, abi });
  } catch {
    return closeOwnedScopes(rawAbiFailure(), closeInstanceScope, closeCallerScope);
  }
  let canonicalBinding;
  try {
    canonicalBinding = createRawCanonicalImportBinding({ adapter, providers, reactor: null });
  } catch {
    return closeOwnedScopes(rawAbiFailure(), closeInstanceScope, closeCallerScope);
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
      physicalProviders,
      plan,
    });
  } catch {
    return closeScope(rawAbiFailure());
  }
  if (signal?.aborted) {
    return closeScope(preInstantiationCancellation());
  }
  let instance;
  try {
    instance = await instantiate(Object.freeze({ module, imports }));
  } catch {
    return closeScope(rawInstantiationFailure());
  }
  try {
    bindInstance(instance);
  } catch {
    return closeScope(rawInstanceBindingFailure());
  }
  let binding;
  try {
    bindRawCanonicalImportInstance({ binding: canonicalBinding, instance });
    binding = bindRawExecutionInstance({ instance, plan });
  } catch {
    return closeScope(rawExportFailure());
  }
  return closeScope(executeCommand({ run: binding.command.run, signal }));
}

async function executeRawProcess({
  contractKey,
  abi,
  adapter,
  module,
  providers,
  physicalProviders,
  instantiate,
  bindInstance,
  signal,
  closeInstanceScope,
  closeCallerScope,
  schedule,
}) {
  let guestEntryEnabled = false;
  let initializationReactorUse = false;
  let observeWake;
  let notifier;
  const reactor = createPollableReactor({
    onReady: token => notifier.notify(token),
    onFailure: () => observeWake(opaqueReactorFailure),
    schedule,
  });
  const closeReactorScope = createExecutionScopeCloser([
    Object.freeze({
      code: "host.raw-reactor",
      message: "The execution host could not close the raw reactor.",
      release: reactor.close,
    }),
  ]);
  const closeBaseScope = outcome => closeOwnedScopes(
    outcome,
    closeReactorScope,
    closeInstanceScope,
    closeCallerScope);

  function assertGuestEntry() {
    if (guestEntryEnabled) return;
    initializationReactorUse = true;
    throw new Error("raw reactor use is unavailable before guest entry");
  }

  let plan;
  try {
    plan = buildRawAbiPlan({ contractKey, abi });
  } catch {
    return closeBaseScope(rawAbiFailure());
  }
  let canonicalBinding;
  try {
    canonicalBinding = createRawCanonicalImportBinding({
      adapter,
      providers,
      reactor: Object.freeze({
        assertAvailable: assertGuestEntry,
        cancel: reactor.cancel,
        module: plan.reactorHostModule,
        watch: reactor.watch,
      }),
    });
  } catch {
    return closeBaseScope(rawAbiFailure());
  }
  const closeCanonicalScope = createExecutionScopeCloser([
    canonicalReleaseAction(canonicalBinding),
  ]);
  const closeScope = outcome => closeOwnedScopes(
    outcome,
    closeReactorScope,
    closeInstanceScope,
    closeCanonicalScope,
    closeCallerScope);
  let imports;
  try {
    imports = composeRawImports({ canonicalBinding, physicalProviders, plan });
  } catch {
    return closeScope(rawAbiFailure());
  }
  if (signal?.aborted) {
    return closeScope(preInstantiationCancellation());
  }

  let instance;
  try {
    instance = await instantiate(Object.freeze({ module, imports }));
  } catch {
    return closeScope(initializationReactorUse
      ? reactorInitializationFailure()
      : rawInstantiationFailure());
  }
  if (initializationReactorUse) {
    return closeScope(reactorInitializationFailure());
  }

  try {
    bindInstance(instance);
  } catch {
    return closeScope(rawInstanceBindingFailure());
  }
  let binding;
  try {
    bindRawCanonicalImportInstance({ binding: canonicalBinding, instance });
    binding = bindRawExecutionInstance({ instance, plan });
    notifier = createGuestWakeNotifier({
      guestWake: binding.reactorGuest.wake,
      observeWake: error => observeWake(error),
    });
  } catch {
    return closeScope(rawExportFailure());
  }
  guestEntryEnabled = true;
  let outcome;
  try {
    outcome = await observeManagedProcess({
      process: binding.process,
      subscribeWake(observer) {
        observeWake = observer;
        return () => { observeWake = undefined; };
      },
      signal,
    });
  } catch {
    outcome = rawExecutionFailure();
  } finally {
    guestEntryEnabled = false;
  }
  return closeScope(outcome);
}

function validateSignal(signal) {
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("raw signal must be an AbortSignal");
  }
}

function rawAbiFailure() {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.raw-abi",
    "The final raw module does not match its execution ABI, generated adapter, or providers.");
}

function canonicalReleaseAction(binding) {
  return Object.freeze({
    code: "host.raw-canonical-imports",
    message: "The execution host could not close the raw canonical imports.",
    release() { closeRawCanonicalImportBinding({ binding }); },
  });
}

function rawInstantiationFailure() {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-instantiate",
    "The execution host could not instantiate the raw module.");
}

function rawInstanceBindingFailure() {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.raw-instance-bind",
    "The execution host could not bind services to the raw instance.");
}

function rawExecutionFailure() {
  return failedExecutionResult(
    "hostFailure",
    "execution",
    "host.raw-execution",
    "The execution host could not execute the raw module.");
}

function rawExportFailure() {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.raw-exports",
    "The instantiated raw module does not expose its declared execution boundary.");
}

function reactorInitializationFailure() {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.reactor-initialization",
    "The raw module used the reactor before guest entry.");
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
