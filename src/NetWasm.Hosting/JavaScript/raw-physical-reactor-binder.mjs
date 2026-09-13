const factoryKeys = [
  "providers",
  "reactor",
  "readStore",
  "releasePollable",
  "transferPollable",
];
const reactorKeys = ["assertAvailable", "cancel", "module", "watch"];
const bindingKeys = [
  "canonicalSignature",
  "coreSignature",
  "kind",
  "parameterMemory",
  "parameters",
  "physical",
  "provider",
  "result",
  "resultMemory",
  "target",
];
const identityKeys = ["module", "name"];
const providerKeys = [
  "function",
  "functionKind",
  "interface",
  "javascriptName",
  "resourceJavaScriptName",
  "resourceName",
  "resourceType",
];
const parameterKeys = ["javascriptName", "name", "type"];
const signatureKeys = [
  "flatParameters",
  "flatResults",
  "indirectParameters",
  "indirectResult",
  "parameters",
  "result",
];
const coreSignatureKeys = ["parameters", "results"];
const reactorProviderInterface = "netwasm:runtime/reactor-host@1.0.0";
const targets = new Set(["wasm32", "wasm64"]);

export function createRawPhysicalReactorBinder(request = {}) {
  const actions = readFactory(request);
  return Object.freeze(binding => bindReactor(binding, actions));
}

function bindReactor(binding, actions) {
  const plan = readBinding(binding, actions.reactor.module);
  if (Object.hasOwn(actions.providers, reactorProviderInterface)) {
    throw new TypeError("raw logical reactor provider is reserved by the executor");
  }
  return plan.operation === "watch"
    ? bindWatch(plan, actions)
    : bindCancel(actions);
}

function bindWatch(plan, actions) {
  return Object.freeze((handle, token) => {
    actions.reactor.assertAvailable();
    validateI32(handle, "pollable handle");
    const logicalToken = readToken(token);
    const pollable = actions.transferPollable({
      handle,
      store: actions.readStore(),
      type: plan.pollableType,
    });
    try {
      actions.reactor.watch(pollable, logicalToken);
    } catch (watchFailure) {
      try {
        actions.releasePollable({ type: plan.pollableType, value: pollable });
      } catch (releaseFailure) {
        throw new AggregateError(
          [watchFailure, releaseFailure],
          "raw reactor rejected a transferred pollable and its release failed");
      }
      throw watchFailure;
    }
  });
}

function bindCancel(actions) {
  return Object.freeze(token => {
    actions.reactor.assertAvailable();
    actions.reactor.cancel(readToken(token));
  });
}

function readBinding(binding, reactorModule) {
  assertExactObject(binding, bindingKeys, "raw physical reactor binding");
  if (binding.kind !== "callable" || !targets.has(binding.target)
      || binding.result !== null || binding.resultMemory !== null) {
    throw new TypeError("raw physical reactor binding is unsupported");
  }
  assertExactObject(binding.physical, identityKeys, "raw physical reactor identity");
  const operation = binding.physical.name;
  if (binding.physical.module !== reactorModule
      || operation !== "watch" && operation !== "cancel") {
    throw new TypeError("raw physical reactor identity is invalid");
  }
  const expectedModule = `${binding.target === "wasm64" ? "cm64p2" : "cm32p2"}`
    + "|netwasm:runtime/reactor-host@1";
  if (reactorModule !== expectedModule) {
    throw new TypeError("raw physical reactor target is invalid");
  }
  readProvider(binding.provider, operation);
  const pollableType = readParameters(binding.parameters, operation);
  readSignatures(binding, operation);
  return Object.freeze({ operation, pollableType });
}

function readProvider(provider, operation) {
  assertExactObject(provider, providerKeys, "raw physical reactor provider");
  if (provider.interface !== reactorProviderInterface
      || provider.function !== operation
      || provider.javascriptName !== operation
      || provider.functionKind !== "freestanding"
      || provider.resourceType !== null
      || provider.resourceName !== null
      || provider.resourceJavaScriptName !== null) {
    throw new TypeError("raw physical reactor provider is invalid");
  }
}

function readParameters(parameters, operation) {
  if (!Array.isArray(parameters) || Object.getOwnPropertySymbols(parameters).length !== 0
      || parameters.length !== (operation === "watch" ? 2 : 1)) {
    throw new TypeError("raw physical reactor parameters are invalid");
  }
  const token = parameters.at(-1);
  assertExactObject(token, parameterKeys, "raw physical reactor parameter");
  assertExactObject(token.type, ["kind"], "raw physical reactor token type");
  if (token.name !== "token" || token.javascriptName !== "token" || token.type.kind !== "u32") {
    throw new TypeError("raw physical reactor token is invalid");
  }
  if (operation === "cancel") return null;
  const pollable = parameters[0];
  assertExactObject(pollable, parameterKeys, "raw physical reactor parameter");
  assertExactObject(
    pollable.type,
    ["kind", "resourceType"],
    "raw physical reactor pollable type");
  if (pollable.name !== "ready" || pollable.javascriptName !== "ready"
      || pollable.type.kind !== "owned-resource"
      || !Number.isSafeInteger(pollable.type.resourceType)
      || pollable.type.resourceType < 0) {
    throw new TypeError("raw physical reactor pollable is invalid");
  }
  return pollable.type.resourceType;
}

function readSignatures(binding, operation) {
  const parameters = operation === "watch" ? ["i32", "i32"] : ["i32"];
  assertExactObject(binding.coreSignature, coreSignatureKeys, "raw physical reactor core signature");
  assertExactObject(binding.canonicalSignature, signatureKeys, "raw physical reactor canonical signature");
  const canonical = binding.canonicalSignature;
  if (!sameValues(binding.coreSignature.parameters, parameters)
      || !sameValues(binding.coreSignature.results, [])
      || !sameValues(canonical.parameters, parameters)
      || !sameValues(canonical.flatParameters, parameters)
      || !sameValues(canonical.flatResults, [])
      || canonical.result !== null
      || canonical.indirectParameters !== false
      || canonical.indirectResult !== false) {
    throw new TypeError("raw physical reactor signature is invalid");
  }
}

function readToken(value) {
  validateI32(value, "token");
  const token = value >>> 0;
  if (token === 0) throw new TypeError("raw reactor token must be nonzero");
  return token;
}

function validateI32(value, label) {
  if (!Number.isInteger(value) || value < -0x8000_0000 || value > 0x7fff_ffff) {
    throw new TypeError(`raw reactor ${label} is not a core i32`);
  }
}

function sameValues(left, right) {
  return Array.isArray(left) && left.length === right.length
    && left.every((value, index) => value === right[index]);
}

function readFactory(request) {
  assertExactObject(request, factoryKeys, "raw physical reactor binder");
  assertPlainDataObject(request.providers, "raw physical reactor providers");
  assertExactObject(request.reactor, reactorKeys, "raw logical reactor");
  if (typeof request.reactor.module !== "string" || request.reactor.module.length === 0
      || typeof request.reactor.assertAvailable !== "function"
      || typeof request.reactor.cancel !== "function"
      || typeof request.reactor.watch !== "function"
      || typeof request.readStore !== "function"
      || typeof request.releasePollable !== "function"
      || typeof request.transferPollable !== "function") {
    throw new TypeError("raw physical reactor binder capabilities are invalid");
  }
  return Object.freeze({
    providers: request.providers,
    reactor: request.reactor,
    readStore: request.readStore,
    releasePollable: request.releasePollable,
    transferPollable: request.transferPollable,
  });
}

function assertPlainDataObject(value, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} are invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== null && prototype !== Object.prototype) {
    throw new TypeError(`${label} must be a plain object`);
  }
  if (Object.values(Object.getOwnPropertyDescriptors(value))
    .some(descriptor => !("value" in descriptor))) {
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
