import { NetWasmHostError } from "./managed-errors.mjs";

const dependencyKeys = [
  "bindCanonicalInstance",
  "buildPlan",
  "closeCanonicalBinding",
  "composeImports",
  "createCanonicalBinding",
  "createEventPump",
  "createReleaseCommand",
  "readComponentModel",
];
// The module the Component Model's own built-ins come from, such as waitable sets.
const componentModelRootModule = "$root";
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
      } = request;
      if (abi === null || typeof abi !== "object" || Array.isArray(abi)
          || adapter === null || typeof adapter !== "object" || Array.isArray(adapter)
          || module === null || typeof module !== "object"
          || providers === null || typeof providers !== "object" || Array.isArray(providers)) {
        throw new TypeError("raw export-session inputs are invalid");
      }
      if (typeof instantiate !== "function" || typeof prepareInterop !== "function") {
        throw new TypeError("raw export-session actions are required");
      }
      validateReleaseActions(releaseActions);

      let plan;
      let state = "opening";
      let terminalFailure = null;
      let closeSession;
      let settleFailure;
      const failure = new Promise(resolve => { settleFailure = resolve; });
      let pump;
      const markFailed = cause => {
        if (terminalFailure !== null || state === "closing" || state === "closed") return;
        pump?.stop();
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
      let interop;
      let canonicalBinding;
      // Released in reverse: the event loop stops before anything it could call closes.
      const ownedReleaseActions = () => [
        ...releaseActions,
        ...(canonicalBinding === undefined ? [] : [() =>
          dependencies.closeCanonicalBinding({ binding: canonicalBinding })]),
        ...(interop === undefined ? [] : [
          interop.close,
          interop.drainTerminalReports,
        ]),
        ...(pump === undefined ? [] : [pump.stop]),
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
          // A JavaScript promise's result reached the module: its loop runs what that queued.
          observeAsyncCompletion(error) {
            if (error === undefined) pump?.turn();
            else markFailed(error);
          },
          observeAsyncFailure: markFailed,
        })));
        if (Object.hasOwn(interop.imports, componentModelRootModule)) {
          throw new TypeError("the Component Model built-ins are reserved by the export session");
        }
        canonicalBinding = dependencies.createCanonicalBinding({ adapter, providers });
        const componentModel = dependencies.readComponentModel({ binding: canonicalBinding });
        const imports = dependencies.composeImports({
          canonicalBinding,
          inventory: Object.freeze({ target: plan.target, imports: plan.imports }),
          physicalProviders: Object.freeze({
            ...interop.imports,
            [componentModelRootModule]: componentModel.root,
          }),
        });
        const result = await instantiate(Object.freeze({ module, imports }));
        throwIfFailed();
        const instance = readInstance(result);
        const boundary = readBoundary(interop.bindInstance(instance));
        throwIfFailed();
        dependencies.bindCanonicalInstance({ binding: canonicalBinding, instance });
        throwIfFailed();
        pump = readEventPump(dependencies.createEventPump(Object.freeze({
          componentModel,
          run: readInstanceFunction(instance, plan.eventLoopRunExport),
          callback: readInstanceFunction(instance, plan.eventLoopCallbackExport),
          onFailure: markFailed,
        })));

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
        pump.turn();
        return Object.freeze({ close, exports: runLoopAfter(boundary.exports, pump.turn), failure });
      } catch (cause) {
        return rollback(cause);
      }
    },
  });
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

function readEventPump(value) {
  if (value === null || typeof value !== "object"
      || typeof value.turn !== "function" || typeof value.stop !== "function") {
    throw new TypeError("raw export-session event pump is invalid");
  }
  return Object.freeze({ turn: value.turn.bind(value), stop: value.stop.bind(value) });
}

// Each call into the module may queue work, which its event loop then runs.
function runLoopAfter(exports, turn) {
  const wrapped = {};
  for (const [name, value] of Object.entries(exports)) {
    wrapped[name] = typeof value !== "function" ? value : (...parameters) => {
      try {
        return value.apply(exports, parameters);
      } finally {
        turn();
      }
    };
  }
  return Object.freeze(wrapped);
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
