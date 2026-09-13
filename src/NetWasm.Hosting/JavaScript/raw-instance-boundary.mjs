const states = new WeakMap();
const factoryKeys = ["target"];
const requirementKeys = ["boundary", "name"];
const bindingKeys = ["boundary", "instance"];
const readKeys = ["boundary"];
const releaseKeys = ["boundary", "resource", "value"];
const resourceKeys = ["destructor", "interface", "intrinsic", "javascriptName", "name", "type"];
const targets = Object.freeze(Object.assign(Object.create(null), {
  wasm32: Object.freeze({ memory: "cm32p2_memory", reallocator: "cm32p2_realloc" }),
  wasm64: Object.freeze({ memory: "cm64p2_memory", reallocator: "cm64p2_realloc" }),
}));

export function createRawInstanceBoundary(request = {}) {
  assertExactObject(request, factoryKeys, "raw instance-boundary factory");
  const target = targets[request.target];
  if (target === undefined) throw new TypeError("raw instance-boundary target is unsupported");
  const boundary = Object.freeze(Object.create(null));
  states.set(boundary, {
    binding: null,
    requirements: new Set(),
    target,
  });
  return boundary;
}

export function requireRawInstanceFunction(request = {}) {
  assertExactObject(request, requirementKeys, "raw instance function requirement");
  const state = readState(request.boundary);
  if (state.binding !== null) {
    throw new TypeError("raw instance requirements are closed after binding");
  }
  if (typeof request.name !== "string" || request.name.length === 0) {
    throw new TypeError("raw instance function requirement name is invalid");
  }
  state.requirements.add(request.name);
}

export function bindRawInstanceBoundary(request = {}) {
  assertExactObject(request, bindingKeys, "raw instance binding");
  const state = readState(request.boundary);
  if (state.binding !== null) throw new TypeError("raw instance boundary is already bound");
  if (request.instance === null || typeof request.instance !== "object") {
    throw new TypeError("raw instance is invalid");
  }
  const exports = request.instance instanceof WebAssembly.Instance
    ? request.instance.exports
    : readDataProperty(request.instance, "exports", "raw instance exports");
  if (exports === null || typeof exports !== "object") {
    throw new TypeError("raw instance exports are invalid");
  }
  const memory = readDataProperty(exports, state.target.memory, "raw canonical memory export");
  if (!(memory instanceof WebAssembly.Memory)) {
    throw new TypeError(`raw canonical memory export '${state.target.memory}' is unavailable`);
  }
  const reallocator = bindExportedFunction(exports, state.target.reallocator, "reallocator");
  const functions = new Map();
  for (const name of state.requirements) {
    functions.set(name, bindExportedFunction(exports, name, "required function"));
  }
  state.binding = Object.freeze({ functions, memory, reallocator });
}

export function readRawInstanceMemory(request = {}) {
  return readBinding(request, "raw instance memory read").memory;
}

export function readRawInstanceReallocator(request = {}) {
  return readBinding(request, "raw instance reallocator read").reallocator;
}

export function releaseRawExportedResource(request = {}) {
  assertExactObject(request, releaseKeys, "raw exported-resource release");
  const binding = requireBinding(readState(request.boundary));
  assertExactObject(request.resource, resourceKeys, "raw exported resource");
  if (typeof request.resource.destructor !== "string"
      || request.resource.destructor.length === 0
      || request.resource.intrinsic === "imported-resource-drop") {
    throw new TypeError("raw exported-resource destructor identity is invalid");
  }
  validateI32(request.value);
  const destructor = binding.functions.get(request.resource.destructor);
  if (destructor === undefined) {
    throw new TypeError("raw exported-resource destructor was not required");
  }
  destructor(request.value);
}

function readBinding(request, label) {
  assertExactObject(request, readKeys, label);
  return requireBinding(readState(request.boundary));
}

function requireBinding(state) {
  if (state.binding === null) throw new TypeError("raw instance boundary is not bound");
  return state.binding;
}

function bindExportedFunction(exports, name, label) {
  const value = readDataProperty(exports, name, `raw ${label} export`);
  if (typeof value !== "function") throw new TypeError(`raw ${label} export '${name}' is unavailable`);
  return Object.freeze(value.bind(exports));
}

function readState(boundary) {
  const state = states.get(boundary);
  if (state === undefined) throw new TypeError("raw instance boundary is invalid");
  return state;
}

function validateI32(value) {
  if (!Number.isInteger(value) || value < -0x8000_0000 || value > 0x7fff_ffff) {
    throw new TypeError("raw exported-resource representation is invalid");
  }
}

function readDataProperty(value, name, label) {
  const descriptor = Object.getOwnPropertyDescriptor(value, name);
  if (descriptor === undefined) return undefined;
  if (!("value" in descriptor)) throw new TypeError(`${label} must be a data property`);
  return descriptor.value;
}

function assertExactObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable
        || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
