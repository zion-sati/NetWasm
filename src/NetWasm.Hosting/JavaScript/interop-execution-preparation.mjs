const states = new WeakMap();
const preparationKeys = ["bindInstance", "close", "createImports", "resolveMemory"];
const bindingKeys = ["instance", "preparation"];
const closeKeys = ["preparation"];

export function prepareInteropExecution(request = {}) {
  assertExactDataObject(request, preparationKeys, "interop preparation request");
  for (const [name, value] of Object.entries(request)) {
    if (typeof value !== "function") {
      throw new TypeError(`interop preparation ${name} action is required`);
    }
  }

  const state = {
    bindInstance: request.bindInstance,
    close: request.close,
    instance: undefined,
    memory: undefined,
    phase: "preparing",
    resolveMemory: request.resolveMemory,
  };
  const readers = Object.freeze({
    readInstance() { return readDeferred(state, "instance"); },
    readMemory() { return readDeferred(state, "memory"); },
  });
  const imports = request.createImports(readers);
  assertPlainDataObject(imports, "interop preparation imports");
  const preparation = Object.freeze({ imports });
  state.phase = "prepared";
  states.set(preparation, state);
  return preparation;
}

export function bindInteropExecutionInstance(request = {}) {
  assertExactDataObject(request, bindingKeys, "interop instance binding request");
  const state = readState(request.preparation);
  if (state.phase !== "prepared") {
    throw new TypeError("interop preparation cannot bind another instance");
  }
  if (request.instance === null || typeof request.instance !== "object") {
    throw new TypeError("interop instance is required");
  }

  state.phase = "binding";
  state.instance = request.instance;
  try {
    state.memory = state.resolveMemory(request.instance);
    const boundary = state.bindInstance();
    if (state.phase !== "binding") {
      throw new TypeError("interop preparation changed state while binding");
    }
    state.phase = "bound";
    return boundary;
  } catch (error) {
    if (state.phase === "binding") state.phase = "failed";
    throw error;
  }
}

export function closeInteropExecution(request = {}) {
  assertExactDataObject(request, closeKeys, "interop close request");
  const state = readState(request.preparation);
  if (state.phase === "closing" || state.phase === "closed") return;
  state.phase = "closing";
  try {
    state.close();
  } finally {
    state.phase = "closed";
    state.instance = undefined;
    state.memory = undefined;
  }
}

function readDeferred(state, name) {
  if (state.phase === "closed") {
    throw new TypeError("interop preparation is closed");
  }
  return state[name];
}

function readState(preparation) {
  const state = preparation !== null && typeof preparation === "object"
    ? states.get(preparation)
    : undefined;
  if (state === undefined) {
    throw new TypeError("interop preparation is invalid");
  }
  return state;
}

function assertPlainDataObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} are invalid`);
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
