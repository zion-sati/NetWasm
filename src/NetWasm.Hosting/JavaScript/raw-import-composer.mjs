import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";

const planKeys = [
  "contractKey",
  "entryPoint",
  "exports",
  "imports",
  "invocationArguments",
  "reactorGuestExport",
  "reactorHostModule",
  "target",
];

export function composeRawImports(request = {}) {
  assertExactObject(
    request,
    ["canonicalBinding", "physicalProviders", "plan"],
    "raw import composition request");
  const { canonicalBinding, physicalProviders, plan } = request;
  validatePlan(plan);
  const canonical = readCanonicalBinding(canonicalBinding, plan.target);
  assertPlainObject(physicalProviders, "raw physical providers");
  if (Object.hasOwn(physicalProviders, plan.reactorHostModule)) {
    throw new TypeError("raw reactor host is reserved by the executor");
  }
  const descriptors = new Map(plan.imports.map(descriptor => [
    identityKey(descriptor.module, descriptor.name),
    descriptor,
  ]));
  for (const [identity, value] of canonical) {
    const descriptor = descriptors.get(identity);
    if (descriptor === undefined) {
      throw new TypeError("generated raw canonical import is absent from the final ABI");
    }
    if (descriptor.kind !== "function" || typeof value !== "function") {
      throw new TypeError("generated raw canonical import is not a final function import");
    }
    rejectPhysicalReplacement(physicalProviders, descriptor.module, descriptor.name);
  }

  const projection = Object.create(null);
  for (const descriptor of plan.imports) {
    const identity = identityKey(descriptor.module, descriptor.name);
    let value = canonical.get(identity);
    if (value === undefined) {
      const source = readDataProperty(physicalProviders, descriptor.module, "raw import provider");
      if (source === null || typeof source !== "object") {
        throw new TypeError(`raw import provider '${descriptor.module}' is unavailable`);
      }
      value = readDataProperty(source, descriptor.name, "raw import member");
      if (value === undefined) {
        throw new TypeError(`raw import '${descriptor.module}.${descriptor.name}' is unavailable`);
      }
    }
    if (descriptor.kind === "function" && typeof value !== "function") {
      throw new TypeError(`raw function import '${descriptor.module}.${descriptor.name}' is invalid`);
    }
    const module = projection[descriptor.module] ?? Object.create(null);
    module[descriptor.name] = value;
    projection[descriptor.module] = module;
  }
  for (const module of Object.values(projection)) Object.freeze(module);
  return Object.freeze(projection);
}

function validatePlan(plan) {
  assertExactObject(plan, planKeys, "raw ABI plan");
  if (plan.contractKey !== commandExecutionContract && plan.contractKey !== processExecutionContract
      || plan.target !== "wasm32" && plan.target !== "wasm64"
      || !Array.isArray(plan.imports)
      || typeof plan.reactorHostModule !== "string") {
    throw new TypeError("raw ABI plan is invalid");
  }
}

function readCanonicalBinding(binding, target) {
  assertExactObject(binding, ["imports", "target"], "raw canonical-import binding");
  if (binding.target !== target) {
    throw new TypeError("raw canonical-import target does not match the final ABI");
  }
  assertPlainDataObject(binding.imports, "raw canonical imports");
  const members = new Map();
  for (const moduleName of Object.keys(binding.imports)) {
    if (moduleName.length === 0) {
      throw new TypeError("raw canonical import module is invalid");
    }
    const module = readDataProperty(binding.imports, moduleName, "raw canonical import module");
    assertPlainDataObject(module, "raw canonical import module");
    for (const name of Object.keys(module)) {
      if (name.length === 0) throw new TypeError("raw canonical import name is invalid");
      const value = readDataProperty(module, name, "raw canonical import");
      if (typeof value !== "function") throw new TypeError("raw canonical import is invalid");
      members.set(identityKey(moduleName, name), value);
    }
  }
  return members;
}

function rejectPhysicalReplacement(providers, moduleName, name) {
  const descriptor = Object.getOwnPropertyDescriptor(providers, moduleName);
  if (descriptor === undefined) return;
  if (!("value" in descriptor) || !descriptor.enumerable) {
    throw new TypeError(`raw import provider '${moduleName}' must be an enumerable data property`);
  }
  const module = descriptor.value;
  if (module !== null && typeof module === "object"
      && Object.getOwnPropertyDescriptor(module, name) !== undefined) {
    throw new TypeError(`raw canonical import '${moduleName}.${name}' cannot be replaced physically`);
  }
}

function identityKey(module, name) {
  return `${module}\u0000${name}`;
}

function assertPlainObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} are invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== null && prototype !== Object.prototype) {
    throw new TypeError(`${label} must be a plain object`);
  }
}

function assertPlainDataObject(value, label) {
  assertPlainObject(value, label);
  if (Object.values(Object.getOwnPropertyDescriptors(value))
    .some(descriptor => !("value" in descriptor) || !descriptor.enumerable)) {
    throw new TypeError(`${label} is invalid`);
  }
}

function readDataProperty(value, name, label) {
  const descriptor = Object.getOwnPropertyDescriptor(value, name);
  if (descriptor === undefined) return undefined;
  if (!("value" in descriptor) || !descriptor.enumerable) {
    throw new TypeError(`${label} '${name}' must be an enumerable data property`);
  }
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
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
