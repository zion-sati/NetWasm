import { NetWasmHostError } from "./managed-errors.mjs";

const dependencyKeys = [
  "createGuestNotifier",
  "createReactor",
  "createReleaseCommand",
];
const requiredRequestKeys = Object.freeze([
  "adapter",
  "imports",
  "loadCoreModule",
]);
const requestKeys = new Set([
  ...requiredRequestKeys,
  "instantiateCore",
  "releaseActions",
  "schedule",
]);

export function createComponentExportSessionOpener(dependencies) {
  assertExactDataObject(
    dependencies,
    dependencyKeys,
    "component export-session opener dependencies");
  for (const [name, value] of Object.entries(dependencies)) {
    if (typeof value !== "function") {
      throw new TypeError(`component export-session opener ${name} action is required`);
    }
  }

  return Object.freeze({
    async open(request = {}) {
      assertRequest(request);
      const {
        adapter,
        imports,
        loadCoreModule,
        instantiateCore,
        releaseActions = [],
        schedule = globalThis.queueMicrotask?.bind(globalThis),
      } = request;
      validateAdapter(adapter);
      assertPlainDataObject(imports, "component export-session imports");
      if (typeof loadCoreModule !== "function"
          || instantiateCore !== undefined && typeof instantiateCore !== "function"
          || typeof schedule !== "function") {
        throw new TypeError("component export-session actions are invalid");
      }
      validateReleaseActions(releaseActions);

      let state = "opening";
      let terminalFailure = null;
      let closeSession;
      let settleFailure;
      const failure = new Promise(resolve => { settleFailure = resolve; });
      const markFailed = cause => {
        if (terminalFailure !== null || state === "closing" || state === "closed") return;
        const shouldClose = state === "ready";
        terminalFailure = cause;
        state = "failed";
        settleFailure(terminalFailure);
        if (shouldClose) {
          Promise.resolve().then(() => closeSession()).catch(() => undefined);
        }
      };
      const throwIfFailed = () => {
        if (terminalFailure !== null) throw terminalFailure;
      };
      const assertAvailable = () => {
        if (terminalFailure !== null) throw terminalFailure;
        if (state !== "ready") {
          throw new NetWasmHostError("NetWasm component export session is closed");
        }
      };
      const assertReactorAvailable = () => {
        if (terminalFailure !== null) throw terminalFailure;
        if (state !== "initializing" && state !== "ready") {
          throw new NetWasmHostError(
            "NetWasm component export-session reactor is unavailable");
        }
      };

      let notifier;
      let reactor;
      const releaseCommand = dependencies.createReleaseCommand([
        ...releaseActions,
        () => reactor?.close(),
      ]);
      const rollback = async cause => {
        state = "closing";
        const failures = await releaseCommand.close();
        state = "closed";
        if (failures.length !== 0) {
          settleFailure(terminalFailure ?? new NetWasmHostError(
            "NetWasm component export session cleanup failed", { cause: failures[0] }));
          throw new AggregateError(
            [cause, ...failures], "NetWasm component export session startup failed");
        }
        settleFailure(terminalFailure);
        throw cause;
      };

      try {
        reactor = readReactor(dependencies.createReactor({
          onReady(token) {
            if (terminalFailure !== null || state === "closing" || state === "closed") return;
            if (notifier === undefined) {
              markFailed(new NetWasmHostError(
                "NetWasm component export-session reactor became ready before binding"));
              return;
            }
            if (notifier === null) {
              markFailed(new NetWasmHostError(
                "NetWasm component export-session reactor has no guest wake binding"));
              return;
            }
            notifier.notify(token);
          },
          onFailure() {
            markFailed(new NetWasmHostError(
              "NetWasm component export-session reactor failed"));
          },
          schedule,
        }));
        throwIfFailed();
        state = "initializing";
        const boundary = readBoundary(await adapter.instantiate(Object.freeze({
          imports,
          instantiateCore,
          loadCoreModule,
          reactorHost: Object.freeze({
            assertAvailable: assertReactorAvailable,
            cancel: reactor.cancel,
            watch: reactor.watch,
          }),
        })));
        throwIfFailed();
        notifier = boundary.guestWake === null
          ? null
          : dependencies.createGuestNotifier({
            guestWake: boundary.guestWake,
            observeWake(error) {
              if (error !== undefined) {
                markFailed(new NetWasmHostError(
                  "NetWasm component export-session guest wake failed"));
              }
            },
          });
        throwIfFailed();

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
                failures, "NetWasm component export session cleanup failed");
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
        const exports = guardExports(boundary.exports, assertAvailable);
        state = "ready";
        return Object.freeze({ close, exports, failure });
      } catch (cause) {
        return rollback(cause);
      }
    },
  });
}

function guardExports(value, assertAvailable) {
  assertPlainDataObject(value, "component export-session exports");
  const exports = Object.create(null);
  for (const [name, member] of Object.entries(value)) {
    if (name.length === 0 || typeof member !== "function") {
      throw new TypeError("component export-session export is invalid");
    }
    exports[name] = (...arguments_) => {
      assertAvailable();
      return member(...arguments_);
    };
  }
  if (Object.keys(exports).length === 0) {
    throw new TypeError("component export-session exports are required");
  }
  return Object.freeze(exports);
}

function readBoundary(value) {
  assertExactDataObject(value, ["exports", "guestWake"],
    "component export-session boundary");
  if (value.guestWake !== null && typeof value.guestWake !== "function") {
    throw new TypeError("component export-session guest wake is invalid");
  }
  return Object.freeze({
    exports: value.exports,
    guestWake: value.guestWake,
  });
}

function readReactor(value) {
  assertExactDataObject(value, ["cancel", "close", "watch"],
    "component export-session reactor");
  if (typeof value.cancel !== "function" || typeof value.close !== "function"
      || typeof value.watch !== "function") {
    throw new TypeError("component export-session reactor actions are invalid");
  }
  return Object.freeze({
    cancel: value.cancel.bind(value),
    close: value.close.bind(value),
    watch: value.watch.bind(value),
  });
}

function validateAdapter(adapter) {
  assertExactDataObject(adapter, ["contractKey", "instantiate"],
    "component export-session adapter");
  if (typeof adapter.contractKey !== "string" || adapter.contractKey.length === 0
      || typeof adapter.instantiate !== "function") {
    throw new TypeError("component export-session adapter is invalid");
  }
}

function validateReleaseActions(actions) {
  if (!Array.isArray(actions) || actions.some(action => typeof action !== "function")) {
    throw new TypeError("component export-session release actions are invalid");
  }
}

function assertRequest(request) {
  if (request === null || typeof request !== "object" || Array.isArray(request)
      || Object.getOwnPropertySymbols(request).length !== 0) {
    throw new TypeError("component export-session opening request is invalid");
  }
  const prototype = Object.getPrototypeOf(request);
  const descriptors = Object.getOwnPropertyDescriptors(request);
  const keys = Object.keys(descriptors);
  if (prototype !== null && prototype !== Object.prototype
      || keys.some(key => !requestKeys.has(key))
      || requiredRequestKeys.some(key => !Object.hasOwn(descriptors, key))
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError("component export-session opening request shape is invalid");
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
