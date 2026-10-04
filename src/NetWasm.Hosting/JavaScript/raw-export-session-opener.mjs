import { NetWasmHostError } from "./managed-errors.mjs";

const dependencyKeys = [
  "bindCanonicalInstance",
  "buildPlan",
  "closeCanonicalBinding",
  "composeImports",
  "createCanonicalBinding",
  "createGuestNotifier",
  "createReactor",
  "createReleaseCommand",
];
const requiredRequestKeys = Object.freeze([
  "abi",
  "adapter",
  "instantiate",
  "manifest",
  "module",
  "prepareInterop",
  "providers",
]);
const requestKeys = new Set([
  ...requiredRequestKeys,
  "releaseActions",
  "schedule",
]);

export function createRawExportSessionOpener(dependencies) {
  assertExactDataObject(dependencies, dependencyKeys, "raw export-session opener dependencies");
  for (const [name, value] of Object.entries(dependencies)) {
    if (typeof value !== "function") {
      throw new TypeError(`raw export-session opener ${name} action is required`);
    }
  }

  return Object.freeze({
    async open(request = {}) {
      assertRequest(request);
      const {
        abi,
        adapter,
        instantiate,
        manifest,
        module,
        prepareInterop,
        providers,
        releaseActions = [],
        schedule = globalThis.queueMicrotask?.bind(globalThis),
      } = request;
      if (abi === null || typeof abi !== "object" || Array.isArray(abi)
          || adapter === null || typeof adapter !== "object" || Array.isArray(adapter)
          || module === null || typeof module !== "object"
          || providers === null || typeof providers !== "object" || Array.isArray(providers)) {
        throw new TypeError("raw export-session inputs are invalid");
      }
      if (typeof instantiate !== "function" || typeof prepareInterop !== "function"
          || typeof schedule !== "function") {
        throw new TypeError("raw export-session actions are required");
      }
      validateReleaseActions(releaseActions);

      let plan;
      let state = "opening";
      let terminalFailure = null;
      let closeSession;
      let settleFailure;
      const failure = new Promise(resolve => { settleFailure = resolve; });
      const markFailed = cause => {
        if (terminalFailure !== null || state === "closing" || state === "closed") return;
        const shouldClose = state === "ready";
        terminalFailure = cause instanceof NetWasmHostError
          ? cause
          : new NetWasmHostError("NetWasm export session failed", { cause });
        state = "failed";
        settleFailure(terminalFailure);
        if (shouldClose) queueTerminalClose();
      };
      const queueTerminalClose = () => {
        Promise.resolve().then(() => closeSession()).catch(() => {
          // The first terminal failure remains the session failure. A later
          // explicit close still observes any cleanup failure.
        });
      };
      const throwIfFailed = () => {
        if (terminalFailure !== null) throw terminalFailure;
      };
      const assertAvailable = () => {
        if (terminalFailure !== null) throw terminalFailure;
        if (state !== "ready") {
          throw new NetWasmHostError(
            state === "closing" || state === "closed"
              ? "NetWasm export session is closed"
              : "NetWasm export session is not ready");
        }
      };
      const assertAsyncDeliveryAvailable = () => {
        if (terminalFailure !== null) throw terminalFailure;
        if (state === "closing" || state === "closed") {
          throw new NetWasmHostError("NetWasm export session is closed");
        }
      };
      const assertReactorAvailable = () => {
        if (terminalFailure !== null) throw terminalFailure;
        if (state !== "initializing" && state !== "ready") {
          throw new NetWasmHostError("NetWasm export-session reactor is unavailable");
        }
      };

      let interop;
      let reactor = null;
      let canonicalBinding;
      const ownedReleaseActions = () => [
        ...releaseActions,
        ...(canonicalBinding === undefined ? [] : [() =>
          dependencies.closeCanonicalBinding({ binding: canonicalBinding })]),
        ...(reactor === null ? [] : [reactor.close]),
        ...(interop === undefined ? [] : [
          interop.close,
          interop.drainTerminalReports,
        ]),
      ];
      const rollback = async cause => {
        state = "closing";
        const failures = await dependencies.createReleaseCommand(ownedReleaseActions()).close();
        state = "closed";
        if (failures.length !== 0) {
          settleFailure(terminalFailure ?? new NetWasmHostError(
            "NetWasm export session cleanup failed", { cause: failures[0] }));
          throw new AggregateError([cause, ...failures], "NetWasm export session startup failed");
        }
        settleFailure(terminalFailure);
        throw cause;
      };

      try {
        plan = dependencies.buildPlan({ abi, manifest });
        interop = readInterop(prepareInterop(Object.freeze({
          assertAsyncDeliveryAvailable,
          assertAvailable,
          observeAsyncFailure: markFailed,
        })));
        let notifier;
        if (requiresReactor(adapter)) {
          reactor = readReactor(dependencies.createReactor({
            onReady(token) {
              if (terminalFailure !== null || state === "closing" || state === "closed") {
                return;
              }
              if (notifier === undefined) {
                markFailed(new NetWasmHostError(
                  "NetWasm export-session reactor became ready before binding"));
                return;
              }
              notifier.notify(token);
            },
            onFailure() {
              markFailed(new NetWasmHostError("NetWasm export-session reactor failed"));
            },
            schedule,
          }));
          throwIfFailed();
        }
        canonicalBinding = dependencies.createCanonicalBinding({
          adapter,
          providers,
          reactor: reactor === null ? null : Object.freeze({
            assertAvailable: assertReactorAvailable,
            cancel: reactor.cancel,
            module: plan.reactorHostModule,
            watch: reactor.watch,
          }),
        });
        const imports = dependencies.composeImports({
          canonicalBinding,
          inventory: Object.freeze({
            target: plan.target,
            imports: plan.imports,
            reactorHostModule: plan.reactorHostModule,
          }),
          physicalProviders: interop.imports,
        });
        const result = await instantiate(Object.freeze({ module, imports }));
        throwIfFailed();
        const instance = readInstance(result);
        const boundary = readBoundary(interop.bindInstance(instance));
        throwIfFailed();
        dependencies.bindCanonicalInstance({ binding: canonicalBinding, instance });
        throwIfFailed();
        if (reactor !== null) {
          const guestWake = readInstanceFunction(instance, plan.reactorGuestExport);
          notifier = dependencies.createGuestNotifier({
            guestWake,
            observeWake(error) {
              if (error !== undefined) {
                markFailed(new NetWasmHostError("NetWasm export-session guest wake failed"));
              }
            },
          });
          throwIfFailed();
        }

        const releaseCommand = dependencies.createReleaseCommand(ownedReleaseActions());
        let closeCompletion;
        const close = () => {
          if (closeCompletion !== undefined) return closeCompletion;
          let resolveClose;
          let rejectClose;
          closeCompletion = new Promise((resolve, reject) => {
            resolveClose = resolve;
            rejectClose = reject;
          });
          state = "closing";
          releaseCommand.close().then(failures => {
            state = "closed";
            if (failures.length !== 0) {
              const cleanupFailure = new AggregateError(
                failures, "NetWasm export session cleanup failed");
              if (terminalFailure === null) settleFailure(cleanupFailure);
              rejectClose(cleanupFailure);
              return;
            }
            if (terminalFailure === null) settleFailure(null);
            resolveClose();
          }, cause => {
            state = "closed";
            if (terminalFailure === null) settleFailure(cause);
            rejectClose(cause);
          });
          return closeCompletion;
        };
        closeSession = close;

        throwIfFailed();
        state = "initializing";
        readInstanceFunction(instance, plan.canonicalInitializeExport)();
        throwIfFailed();
        state = "ready";
        return Object.freeze({ close, exports: boundary.exports, failure });
      } catch (cause) {
        return rollback(cause);
      }
    },
  });
}

function requiresReactor(adapter) {
  const metadata = readDataProperty(adapter, "rawAdapterMetadata");
  const capabilities = metadata === null || typeof metadata !== "object"
    ? undefined
    : readDataProperty(metadata, "requiredCapabilities");
  return Array.isArray(capabilities) && capabilities.includes("bindReactor");
}

function readInterop(value) {
  assertExactDataObject(value, [
    "bindInstance",
    "close",
    "consumeTerminalEvent",
    "drainTerminalReports",
    "imports",
  ],
    "raw export-session interop");
  if (typeof value.bindInstance !== "function" || typeof value.close !== "function"
      || typeof value.consumeTerminalEvent !== "function"
      || typeof value.drainTerminalReports !== "function") {
    throw new TypeError("raw export-session interop actions are invalid");
  }
  assertPlainDataObject(value.imports, "raw export-session interop imports");
  return Object.freeze({
    bindInstance: value.bindInstance.bind(value),
    close: value.close.bind(value),
    consumeTerminalEvent: value.consumeTerminalEvent.bind(value),
    drainTerminalReports: value.drainTerminalReports.bind(value),
    imports: value.imports,
  });
}

function readReactor(value) {
  assertExactDataObject(value, ["cancel", "close", "watch"], "raw export-session reactor");
  if (typeof value.cancel !== "function" || typeof value.close !== "function"
      || typeof value.watch !== "function") {
    throw new TypeError("raw export-session reactor actions are invalid");
  }
  return Object.freeze({
    cancel: value.cancel.bind(value),
    close: value.close.bind(value),
    watch: value.watch.bind(value),
  });
}

function readInstance(result) {
  const instance = result instanceof WebAssembly.Instance ? result : result?.instance ?? result;
  if (instance === null || typeof instance !== "object"
      || instance.exports === null || typeof instance.exports !== "object") {
    throw new TypeError("raw export-session instantiator returned an invalid instance");
  }
  return instance;
}

function readBoundary(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || value.exports === null || typeof value.exports !== "object"
      || Array.isArray(value.exports)) {
    throw new TypeError("raw export-session interop boundary is invalid");
  }
  return value;
}

function readInstanceFunction(instance, name) {
  const value = readDataProperty(instance.exports, name);
  if (typeof value !== "function") {
    throw new TypeError(`raw export-session function '${name}' is unavailable`);
  }
  return value.bind(instance.exports);
}

function readDataProperty(value, name) {
  const descriptor = Object.getOwnPropertyDescriptor(value, name);
  if (descriptor === undefined) return undefined;
  if (!("value" in descriptor) || !descriptor.enumerable) {
    throw new TypeError(`raw export-session property '${name}' must be an enumerable data property`);
  }
  return descriptor.value;
}

function validateReleaseActions(actions) {
  if (!Array.isArray(actions) || actions.some(action => typeof action !== "function")) {
    throw new TypeError("raw export-session release actions are invalid");
  }
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("raw export-session opening request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError("raw export-session opening request shape is invalid");
  }
}

function assertPlainDataObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== null && prototype !== Object.prototype
      || Object.values(Object.getOwnPropertyDescriptors(value))
        .some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} must be a plain data object`);
  }
}

function assertExactDataObject(value, keys, label) {
  assertPlainDataObject(value, label);
  const actualKeys = Object.keys(value).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
