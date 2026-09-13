const factoryKeys = ["providers"];
const providerKeys = [
  "function",
  "functionKind",
  "interface",
  "javascriptName",
  "resourceJavaScriptName",
  "resourceName",
  "resourceType",
];
const providerStrategies = Object.freeze(Object.assign(Object.create(null), {
  freestanding: bindFreestanding,
  constructor: bindConstructor,
  method: bindMethod,
  static: bindStatic,
}));

export function createRawProviderCallableBinder(request = {}) {
  assertExactObject(request, factoryKeys, "raw provider-callable binder");
  assertPlainObject(request.providers, "raw logical providers");
  return Object.freeze(binding => bindProviderCallable(binding, request.providers));
}

function bindProviderCallable(binding, providers) {
  if (binding === null || typeof binding !== "object" || Array.isArray(binding)) {
    throw new TypeError("raw callable binding is invalid");
  }
  const provider = readDataProperty(binding, "provider", "raw callable provider");
  assertExactObject(provider, providerKeys, "raw callable provider");
  const interfaceName = readName(provider.interface, true, "interface");
  readName(provider.function, false, "function");
  const javaScriptName = readName(provider.javascriptName, false, "JavaScript function");
  const source = readDataProperty(providers, interfaceName, "raw logical provider");
  if (source === null || typeof source !== "object") {
    throw new TypeError(`raw logical provider '${interfaceName}' is unavailable`);
  }

  const strategy = providerStrategies[provider.functionKind];
  if (strategy === undefined) {
    throw new TypeError("raw provider function kind is unsupported");
  }
  return strategy(source, provider, javaScriptName);
}

function bindFreestanding(source, provider, javaScriptName) {
  requireAbsentResource(provider);
  return bindMember(source, javaScriptName, "raw provider function");
}

function bindConstructor(source, provider, javaScriptName) {
  const resource = readResourceProvider(source, provider);
  if (javaScriptName !== provider.resourceJavaScriptName) {
    throw new TypeError("raw provider constructor identity is invalid");
  }
  return Object.freeze(parameters => {
    validateParameters(parameters);
    return Reflect.construct(resource, parameters);
  });
}

function bindStatic(source, provider, javaScriptName) {
  return bindMember(
    readResourceProvider(source, provider),
    javaScriptName,
    "raw provider static function");
}

function bindMethod(source, provider, javaScriptName) {
  const resource = readResourceProvider(source, provider);
  requirePrototypeMember(resource, javaScriptName);
  return Object.freeze(parameters => {
    validateParameters(parameters);
    if (parameters.length === 0 || !(parameters[0] instanceof resource)) {
      throw new TypeError("raw provider method receiver is invalid");
    }
    const receiver = parameters[0];
    const method = readInheritedDataProperty(receiver, javaScriptName, "raw provider method");
    if (typeof method !== "function") throw new TypeError("raw provider method is invalid");
    return method.apply(receiver, parameters.slice(1));
  });
}

function readResourceProvider(source, provider) {
  if (!Number.isSafeInteger(provider.resourceType) || provider.resourceType < 0
      || typeof provider.resourceName !== "string" || provider.resourceName.length === 0
      || typeof provider.resourceJavaScriptName !== "string"
      || provider.resourceJavaScriptName.length === 0) {
    throw new TypeError("raw callable resource provider is invalid");
  }
  const resource = readDataProperty(
    source,
    provider.resourceJavaScriptName,
    "raw provider resource");
  if (typeof resource !== "function") {
    throw new TypeError(`raw provider resource '${provider.resourceJavaScriptName}' is unavailable`);
  }
  return resource;
}

function requireAbsentResource(provider) {
  if (provider.resourceType !== null || provider.resourceName !== null
      || provider.resourceJavaScriptName !== null) {
    throw new TypeError("raw freestanding provider contains a resource identity");
  }
}

function bindMember(owner, name, label) {
  const member = readDataProperty(owner, name, label);
  if (typeof member !== "function") throw new TypeError(`${label} is invalid`);
  return Object.freeze(parameters => {
    validateParameters(parameters);
    return member.apply(owner, parameters);
  });
}

function requirePrototypeMember(resource, name) {
  const member = readInheritedDataProperty(resource.prototype, name, "raw provider method");
  if (typeof member !== "function") throw new TypeError("raw provider method is invalid");
}

function readInheritedDataProperty(value, name, label) {
  let current = value;
  while (current !== null) {
    const descriptor = Object.getOwnPropertyDescriptor(current, name);
    if (descriptor !== undefined) {
      if (!("value" in descriptor)) throw new TypeError(`${label} must be a data property`);
      return descriptor.value;
    }
    current = Object.getPrototypeOf(current);
  }
  return undefined;
}

function validateParameters(value) {
  if (!Array.isArray(value)) throw new TypeError("raw provider parameters are invalid");
}

function readName(value, allowEmpty, label) {
  if (typeof value !== "string" || !allowEmpty && value.length === 0) {
    throw new TypeError(`raw callable provider ${label} is invalid`);
  }
  return value;
}

function readDataProperty(value, name, label) {
  const descriptor = Object.getOwnPropertyDescriptor(value, name);
  if (descriptor === undefined) return undefined;
  if (!("value" in descriptor)) throw new TypeError(`${label} must be a data property`);
  return descriptor.value;
}

function assertPlainObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const descriptors = Object.values(Object.getOwnPropertyDescriptors(value));
  if (descriptors.some(descriptor => !("value" in descriptor))) {
    throw new TypeError(`${label} must contain only data properties`);
  }
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
