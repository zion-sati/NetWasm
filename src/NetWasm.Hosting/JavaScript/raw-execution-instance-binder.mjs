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

export function bindRawExecutionInstance(request = {}) {
  assertExactObject(request, ["instance", "plan"], "raw execution-instance binding request");
  const { instance, plan } = request;
  validatePlan(plan);
  if (instance === null || typeof instance !== "object"
      || instance.exports === null || typeof instance.exports !== "object") {
    throw new TypeError("raw instance exports are required");
  }
  const run = requireInstanceFunction(instance.exports, "run");
  const invoke = () => run(...plan.invocationArguments);
  if (plan.contractKey === commandExecutionContract) {
    return Object.freeze({
      contractKey: plan.contractKey,
      command: Object.freeze({
        run: plan.entryPoint.returnShape === "void" ? () => { invoke(); return 0; } : invoke,
      }),
    });
  }

  const status = requireInstanceFunction(instance.exports, "netwasm.process.status");
  const complete = requireInstanceFunction(instance.exports, "netwasm.process.complete");
  const wake = requireInstanceFunction(instance.exports, plan.reactorGuestExport);
  const result = plan.entryPoint.returnShape === "exitCode"
    ? requireInstanceFunction(instance.exports, "netwasm.process.result")
    : () => 0;
  return Object.freeze({
    contractKey: processExecutionContract,
    process: Object.freeze({ start: invoke, status, exitCode: result, complete }),
    reactorGuest: Object.freeze({ wake }),
  });
}

function validatePlan(plan) {
  assertExactObject(plan, planKeys, "raw ABI plan");
  if (plan.contractKey !== commandExecutionContract && plan.contractKey !== processExecutionContract
      || plan.entryPoint === null || typeof plan.entryPoint !== "object"
      || !Array.isArray(plan.invocationArguments)
      || typeof plan.reactorGuestExport !== "string") {
    throw new TypeError("raw ABI plan is invalid");
  }
}

function requireInstanceFunction(exports, name) {
  const value = readDataProperty(exports, name, "raw instance export");
  if (typeof value !== "function") {
    throw new TypeError(`raw instance function '${name}' is unavailable`);
  }
  return value.bind(exports);
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
