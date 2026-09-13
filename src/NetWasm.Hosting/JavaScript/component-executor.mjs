import {
  bindCanonicalComponent,
  commandComponentContract,
  processComponentContract,
} from "./canonical-component-binder.mjs";
import { executeCommand } from "./command-executor.mjs";
import { prepareComponentExecution } from "./component-execution-preparation.mjs";
import { createExecutionScopeCloser } from "./execution-scope-closer.mjs";
import { failedExecutionResult } from "./execution-result.mjs";
import { createGuestWakeNotifier } from "./guest-wake-notifier.mjs";
import { observeManagedProcess } from "./managed-process-observer.mjs";
import { createPollableReactor } from "./pollable-reactor.mjs";

const reactorHostModule = "netwasm:runtime/reactor-host";
const adapterKeys = ["contractKey", "instantiate"];
const opaqueReactorFailure = Object.freeze({});

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
    signal = null,
    releaseActions = [],
    schedule = globalThis.queueMicrotask?.bind(globalThis),
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
    schedule,
  });

  if (adapter.contractKey !== contractKey) {
    return prepared.closeCallerScope(failedExecutionResult(
      "contractFailure",
      "instantiation",
      "contract.component-adapter",
      "The generated component adapter does not match the execution contract."));
  }
  if (signal?.aborted) {
    return prepared.closeCallerScope(failedExecutionResult(
      "callerCancellation",
      "instantiation",
      "caller.cancelled",
      "Component execution was cancelled by the caller before instantiation."));
  }

  if (contractKey === commandComponentContract) {
    return executeCommandComponent({
      adapter,
      projectedImports: prepared.imports,
      loadCoreModule,
      instantiateCore: prepared.instantiateCore,
      signal: prepared.signal,
      closeScope: prepared.closeCallerScope,
    });
  }
  return executeProcessComponent({
    adapter,
    projectedImports: prepared.imports,
    loadCoreModule,
    instantiateCore: prepared.instantiateCore,
    signal: prepared.signal,
    closeCallerScope: prepared.closeCallerScope,
    schedule: prepared.schedule,
  });
}

async function executeCommandComponent({
  adapter,
  projectedImports,
  loadCoreModule,
  instantiateCore,
  signal,
  closeScope,
}) {
  let instance;
  try {
    instance = await adapter.instantiate(Object.freeze({
      loadCoreModule,
      imports: projectedImports,
      instantiateCore,
    }));
  } catch {
    return closeScope(componentInstantiationFailure());
  }

  let binding;
  try {
    binding = bindCanonicalComponent({
      contractKey: commandComponentContract,
      instance,
    });
  } catch {
    return closeScope(componentBindingFailure());
  }
  return closeScope(executeCommand({ run: binding.command.run, signal }));
}

async function executeProcessComponent({
  adapter,
  projectedImports,
  loadCoreModule,
  instantiateCore,
  signal,
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
  const reactorHost = Object.freeze({
    watch(pollable, token) {
      assertGuestEntry();
      return reactor.watch(pollable, token);
    },
    cancel(token) {
      assertGuestEntry();
      return reactor.cancel(token);
    },
  });
  const closeReactorScope = createExecutionScopeCloser([
    {
      code: "host.component-reactor",
      message: "The execution host could not close the component reactor.",
      release: reactor.close,
    },
  ]);
  const closeScope = async outcome => closeCallerScope(await closeReactorScope(outcome));
  const imports = Object.freeze(Object.assign(Object.create(null), projectedImports, {
    [reactorHostModule]: reactorHost,
  }));

  function assertGuestEntry() {
    if (guestEntryEnabled) return;
    initializationReactorUse = true;
    throw new Error("component reactor use is unavailable before guest entry");
  }

  let instance;
  try {
    instance = await adapter.instantiate(Object.freeze({
      loadCoreModule,
      imports,
      instantiateCore,
    }));
  } catch {
    return closeScope(initializationReactorUse
      ? reactorInitializationFailure()
      : componentInstantiationFailure());
  }
  if (initializationReactorUse) {
    return closeScope(reactorInitializationFailure());
  }

  let binding;
  try {
    binding = bindCanonicalComponent({
      contractKey: processComponentContract,
      instance,
    });
  } catch {
    return closeScope(componentBindingFailure());
  }

  notifier = createGuestWakeNotifier({
    guestWake: binding.reactorGuest.wake,
    observeWake: error => observeWake(error),
  });
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
  } finally {
    guestEntryEnabled = false;
  }
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

function componentInstantiationFailure() {
  return failedExecutionResult(
    "hostFailure",
    "instantiation",
    "host.component-instantiate",
    "The execution host could not instantiate the component.");
}

function componentBindingFailure() {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.component-exports",
    "The generated component adapter returned an invalid canonical boundary.");
}

function reactorInitializationFailure() {
  return failedExecutionResult(
    "contractFailure",
    "instantiation",
    "contract.reactor-initialization",
    "The component used the reactor before guest entry.");
}
