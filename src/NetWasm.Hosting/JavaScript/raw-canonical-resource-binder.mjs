const factoryKeys = [
  "borrowResource",
  "dropResource",
  "readStore",
  "registerResource",
  "releaseExportedResource",
  "requireInstanceFunction",
];
const bindingKeys = ["coreSignature", "kind", "physical", "resource", "target"];
const resourceKeys = ["destructor", "interface", "intrinsic", "javascriptName", "name", "type"];
const signatureKeys = ["parameters", "results"];
const targets = new Set(["wasm32", "wasm64"]);
const strategies = Object.freeze(Object.assign(Object.create(null), {
  "imported-resource-drop": Object.freeze({ bind: bindDrop, returnsHandle: false }),
  "exported-resource-new": Object.freeze({ bind: bindNew, returnsHandle: true }),
  "exported-resource-rep": Object.freeze({ bind: bindRepresentation, returnsHandle: true }),
  "exported-resource-drop": Object.freeze({ bind: bindDrop, returnsHandle: false }),
}));

export function createRawCanonicalResourceBinder(request = {}) {
  const actions = readFactory(request);
  return Object.freeze(binding => bindResource(binding, actions));
}

function bindResource(binding, actions) {
  const resource = readBinding(binding);
  if (resource.destructor !== null) actions.requireInstanceFunction(resource.destructor);
  return strategies[resource.intrinsic].bind(resource, actions);
}

function bindDrop(resource, actions) {
  return Object.freeze(handle => {
    validateI32(handle);
    actions.dropResource({ handle, store: actions.readStore(), type: resource.type });
  });
}

function bindRepresentation(resource, actions) {
  return Object.freeze(handle => {
    validateI32(handle);
    const representation = actions.borrowResource({
      handle,
      store: actions.readStore(),
      type: resource.type,
    });
    validateI32(representation);
    return representation;
  });
}

function bindNew(resource, actions) {
  return Object.freeze(representation => {
    validateI32(representation);
    const store = actions.readStore();
    const handle = actions.registerResource({
      release: value => actions.releaseExportedResource({ resource, value }),
      store,
      type: resource.type,
      value: representation,
    });
    validateHandle(handle);
    return handle | 0;
  });
}

function readBinding(binding) {
  assertExactObject(binding, bindingKeys, "raw canonical resource binding");
  if (binding.kind !== "resource" || !targets.has(binding.target)) {
    throw new TypeError("raw canonical resource binding is unsupported");
  }
  assertExactObject(binding.resource, resourceKeys, "raw canonical resource");
  const resource = binding.resource;
  if (typeof resource.interface !== "string"
      || typeof resource.name !== "string" || resource.name.length === 0
      || typeof resource.javascriptName !== "string" || resource.javascriptName.length === 0
      || !Number.isSafeInteger(resource.type) || resource.type < 0) {
    throw new TypeError("raw canonical resource identity is invalid");
  }
  const intrinsic = strategies[resource.intrinsic];
  if (intrinsic === undefined) throw new TypeError("raw resource intrinsic is unsupported");
  if (intrinsic === strategies["imported-resource-drop"]
    ? resource.destructor !== null
    : typeof resource.destructor !== "string" || resource.destructor.length === 0) {
    throw new TypeError("raw resource destructor identity is invalid");
  }
  assertExactObject(binding.coreSignature, signatureKeys, "raw resource core signature");
  const results = intrinsic.returnsHandle ? ["i32"] : [];
  if (!sameValues(binding.coreSignature.parameters, ["i32"])
      || !sameValues(binding.coreSignature.results, results)) {
    throw new TypeError("raw resource intrinsic signature is invalid");
  }
  return Object.freeze({
    destructor: resource.destructor,
    interface: resource.interface,
    intrinsic: resource.intrinsic,
    javascriptName: resource.javascriptName,
    name: resource.name,
    type: resource.type,
  });
}

function validateI32(value) {
  if (!Number.isInteger(value) || value < -0x8000_0000 || value > 0x7fff_ffff) {
    throw new TypeError("raw resource core i32 value is invalid");
  }
}

function validateHandle(value) {
  if (!Number.isInteger(value) || value <= 0 || value > 0xffff_ffff) {
    throw new TypeError("raw registered resource handle is invalid");
  }
}

function sameValues(left, right) {
  return Array.isArray(left) && left.length === right.length
    && left.every((value, index) => value === right[index]);
}

function readFactory(request) {
  assertExactObject(request, factoryKeys, "raw canonical resource binder");
  if (factoryKeys.some(key => typeof request[key] !== "function")) {
    throw new TypeError("raw canonical resource binder capabilities are invalid");
  }
  return Object.freeze(Object.fromEntries(factoryKeys.map(key => [key, request[key]])));
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
