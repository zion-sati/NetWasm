const factoryKeys = [
  "bindProvider",
  "liftFlatValue",
  "lowerFlatValue",
  "projectMemoryRange",
  "readMemory",
  "readMemoryValue",
  "readStore",
  "writeMemoryValue",
];
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
const signatureKeys = [
  "flatParameters",
  "flatResults",
  "indirectParameters",
  "indirectResult",
  "parameters",
  "result",
];
const coreSignatureKeys = ["parameters", "results"];
const coreKinds = new Set(["f32", "f64", "i32", "i64"]);
const targets = new Set(["wasm32", "wasm64"]);

export function createRawCanonicalCallableBinder(request = {}) {
  const actions = readFactory(request);
  return Object.freeze(binding => bindCallable(binding, actions));
}

function bindCallable(binding, actions) {
  const plan = readBinding(binding);
  const invokeProvider = actions.bindProvider(binding);
  if (typeof invokeProvider !== "function") {
    throw new TypeError("raw provider callable binding is invalid");
  }
  return Object.freeze((...coreArguments) => invoke(plan, coreArguments, invokeProvider, actions));
}

function invoke(plan, coreArguments, invokeProvider, actions) {
  validateCoreValues(coreArguments, plan.coreParameters);
  const memory = actions.readMemory();
  const store = actions.readStore();
  const resultAddress = plan.indirectResult
    ? coreArguments[coreArguments.length - 1]
    : undefined;
  if (plan.indirectResult) {
    actions.projectMemoryRange({
      address: resultAddress,
      alignment: plan.resultMemory.alignment,
      byteLength: plan.resultMemory.size,
      memory,
      target: plan.target,
    });
  }
  const parameterArguments = coreArguments.slice(0, plan.parameterCoreCount);
  const parameters = plan.indirectParameters
    ? actions.readMemoryValue({
      address: parameterArguments[0],
      memory,
      store,
      target: plan.target,
      type: plan.parameterType,
    })
    : actions.liftFlatValue({
      memory,
      store,
      target: plan.target,
      type: plan.parameterType,
      values: parameterArguments,
    });
  if (!Array.isArray(parameters) || parameters.length !== plan.parameterCount) {
    throw new TypeError("raw lifted provider parameters are invalid");
  }

  const value = invokeLogicalResult(plan.result, invokeProvider, parameters);
  if (plan.result === null) return undefined;
  if (plan.indirectResult) {
    actions.writeMemoryValue({
      address: resultAddress,
      memory,
      store,
      target: plan.target,
      type: plan.result,
      value,
    });
    return undefined;
  }
  const results = actions.lowerFlatValue({
    memory,
    store,
    target: plan.target,
    type: plan.result,
    value,
  });
  if (!Array.isArray(results) || results.length !== plan.coreResults.length) {
    throw new TypeError("raw lowered provider result is invalid");
  }
  return results.length === 0 ? undefined : results[0];
}

function invokeLogicalResult(result, invokeProvider, parameters) {
  if (result?.kind !== "result") return invokeProvider(parameters);
  try {
    return taggedResult("ok", result.ok, invokeProvider(parameters));
  } catch (error) {
    return taggedResult("err", result.error, errorPayload(error));
  }
}

function taggedResult(tag, type, value) {
  const result = Object.create(null);
  result.tag = tag;
  if (type !== null) result.val = value;
  return Object.freeze(result);
}

function errorPayload(error) {
  if (error !== null && (typeof error === "object" || typeof error === "function")) {
    const descriptor = Object.getOwnPropertyDescriptor(error, "payload");
    if (descriptor !== undefined) {
      if (!("value" in descriptor)) {
        throw new TypeError("raw provider error payload must be a data property");
      }
      return descriptor.value;
    }
  }
  if (error instanceof Error) throw error;
  return error;
}

function readBinding(binding) {
  assertExactObject(binding, bindingKeys, "raw canonical callable binding");
  if (binding.kind !== "callable" || !targets.has(binding.target)) {
    throw new TypeError("raw canonical callable binding is unsupported");
  }
  const core = readCoreSignature(binding.coreSignature);
  const canonical = readCanonicalSignature(binding.canonicalSignature);
  if (!sameValues(core.parameters, canonical.parameters)
      || !sameValues(core.results, canonical.result === null ? [] : [canonical.result])) {
    throw new TypeError("raw canonical callable signatures disagree");
  }
  const parameters = readParameters(binding.parameters);
  const parameterCoreCount = canonical.indirectParameters ? 1 : canonical.flatParameters.length;
  const expectedParameterCount = parameterCoreCount + (canonical.indirectResult ? 1 : 0);
  if (core.parameters.length !== expectedParameterCount
      || canonical.indirectParameters && parameters.length === 0
      || canonical.indirectResult !== (canonical.flatResults.length > 1)
      || canonical.indirectResult && binding.result === null
      || canonical.indirectResult && binding.resultMemory === null
      || !canonical.indirectResult && binding.resultMemory !== null && binding.result === null
      || !canonical.indirectResult && canonical.flatResults.length > 1
      || !canonical.indirectResult
        && canonical.result !== (canonical.flatResults.length === 1
          ? canonical.flatResults[0]
          : null)
      || binding.result === null && canonical.flatResults.length !== 0
      || binding.result !== null && canonical.flatResults.length === 0
        && binding.result.kind !== "unit") {
    throw new TypeError("raw canonical callable layout is invalid");
  }
  if (canonical.indirectResult) readMemoryLayout(binding.resultMemory, "result");
  readMemoryLayout(binding.parameterMemory, "parameter");
  return Object.freeze({
    coreParameters: core.parameters,
    coreResults: core.results,
    indirectParameters: canonical.indirectParameters,
    indirectResult: canonical.indirectResult,
    parameterCoreCount,
    parameterCount: parameters.length,
    parameterType: Object.freeze({
      fields: Object.freeze(parameters.map(parameter => Object.freeze({
        name: parameter.name,
        type: parameter.type,
      }))),
      kind: "tuple",
    }),
    result: binding.result,
    resultMemory: binding.resultMemory,
    target: binding.target,
  });
}

function readParameters(value) {
  if (!Array.isArray(value) || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError("raw callable parameters are invalid");
  }
  return value.map(parameter => {
    assertExactObject(parameter, ["javascriptName", "name", "type"], "raw callable parameter");
    if (typeof parameter.name !== "string" || parameter.name.length === 0
        || typeof parameter.javascriptName !== "string" || parameter.javascriptName.length === 0
        || parameter.type === null || typeof parameter.type !== "object") {
      throw new TypeError("raw callable parameter is invalid");
    }
    return parameter;
  });
}

function readCanonicalSignature(value) {
  assertExactObject(value, signatureKeys, "raw canonical signature");
  const parameters = readCoreKinds(value.parameters, "canonical parameters");
  const flatParameters = readCoreKinds(value.flatParameters, "flat parameters");
  const flatResults = readCoreKinds(value.flatResults, "flat results");
  if (value.result !== null && !coreKinds.has(value.result)
      || typeof value.indirectParameters !== "boolean"
      || typeof value.indirectResult !== "boolean") {
    throw new TypeError("raw canonical signature is invalid");
  }
  return { ...value, flatParameters, flatResults, parameters };
}

function readCoreSignature(value) {
  assertExactObject(value, coreSignatureKeys, "raw core signature");
  const parameters = readCoreKinds(value.parameters, "core parameters");
  const results = readCoreKinds(value.results, "core results");
  if (results.length > 1) throw new TypeError("raw core signature has multiple results");
  return { parameters, results };
}

function readCoreKinds(value, label) {
  if (!Array.isArray(value) || value.some(kind => !coreKinds.has(kind))) {
    throw new TypeError(`raw ${label} are invalid`);
  }
  return Object.freeze([...value]);
}

function readMemoryLayout(value, label) {
  if (value === null || typeof value !== "object"
      || !Number.isSafeInteger(value.size) || value.size < 0
      || !Number.isSafeInteger(value.alignment) || value.alignment <= 0) {
    throw new TypeError(`raw callable ${label} memory layout is invalid`);
  }
}

function validateCoreValues(values, kinds) {
  if (values.length !== kinds.length) throw new TypeError("raw callable core arity is invalid");
  values.forEach((value, index) => {
    const kind = kinds[index];
    if (kind === "i32" && (!Number.isInteger(value)
        || value < -0x8000_0000 || value > 0x7fff_ffff)) {
      throw new TypeError("raw callable core i32 value is invalid");
    }
    if (kind === "i64" && (typeof value !== "bigint"
        || value < -0x8000_0000_0000_0000n || value > 0x7fff_ffff_ffff_ffffn)) {
      throw new TypeError("raw callable core i64 value is invalid");
    }
    if ((kind === "f32" || kind === "f64") && typeof value !== "number") {
      throw new TypeError(`raw callable core ${kind} value is invalid`);
    }
  });
}

function sameValues(left, right) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function readFactory(request) {
  assertExactObject(request, factoryKeys, "raw canonical callable binder");
  if (factoryKeys.some(key => typeof request[key] !== "function")) {
    throw new TypeError("raw canonical callable binder capabilities are invalid");
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
