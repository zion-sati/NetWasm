import { NetWasmHostError } from "./managed-errors.mjs";

const dependencyKeys = [
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
      } = request;
      validateAdapter(adapter);
      assertPlainDataObject(imports, "component export-session imports");
      if (typeof loadCoreModule !== "function"
          || instantiateCore !== undefined && typeof instantiateCore !== "function") {
        throw new TypeError("component export-session actions are invalid");
      }
      validateReleaseActions(releaseActions);

      // The component's own exports run its event loop, so nothing fails the session
      // between calls; its failure settles when it closes.
      let state = "opening";
      let settleFailure;
      const failure = new Promise(resolve => { settleFailure = resolve; });
      const assertAvailable = () => {
        if (state !== "ready") {
          throw new NetWasmHostError("NetWasm component export session is closed");
        }
      };
      const releaseCommand = dependencies.createReleaseCommand([...releaseActions]);
      const rollback = async cause => {
        state = "closing";
        const failures = await releaseCommand.close();
        state = "closed";
        if (failures.length !== 0) {
          settleFailure(new NetWasmHostError(
            "NetWasm component export session cleanup failed", { cause: failures[0] }));
          throw new AggregateError(
            [cause, ...failures], "NetWasm component export session startup failed");
        }
        settleFailure(null);
        throw cause;
      };

      try {
        state = "initializing";
        const boundary = readBoundary(await adapter.instantiate(Object.freeze({
          imports,
          instantiateCore,
          loadCoreModule,
        })));

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
              settleFailure(cleanupFailure);
              rejectClose(cleanupFailure);
              return;
            }
            settleFailure(null);
            resolveClose();
          }, cause => {
            state = "closed";
            settleFailure(cause);
            rejectClose(cause);
          });
          return closeCompletion;
        };
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
  assertExactDataObject(value, ["exports"], "component export-session boundary");
  return Object.freeze({ exports: value.exports });
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
