import { commandExecutionContract } from "./execution-contracts.mjs";

const planKeys = [
  "contractKey",
  "entryPoint",
  "exports",
  "imports",
  "invocationArguments",
  "target",
];

// An asynchronous Main is callback-lifted: `run` starts it and returns a callback code, the
// host's Component Model hands its callback the events it waits for until it exits, and the
// exit-code export reads Main's result.
export function bindRawExecutionInstance(request = {}) {
  assertExactObject(request, ["componentModel", "instance", "plan"],
    "raw execution-instance binding request");
  const { componentModel, instance, plan } = request;
  validatePlan(plan);
  if (instance === null || typeof instance !== "object"
      || instance.exports === null || typeof instance.exports !== "object") {
    throw new TypeError("raw instance exports are required");
  }
  const run = requireInstanceFunction(instance.exports, "run");
  const invoke = () => run(...plan.invocationArguments);
  if (plan.entryPoint.completionShape === "synchronous") {
    return Object.freeze({
      contractKey: plan.contractKey,
      command: Object.freeze({
        run: plan.entryPoint.returnShape === "void" ? () => { invoke(); return 0; } : invoke,
      }),
    });
  }
  if (componentModel === null || typeof componentModel !== "object"
      || typeof componentModel.drive !== "function") {
    throw new TypeError("raw Component Model is required for an asynchronous entry point");
  }
  const callback = requireInstanceFunction(instance.exports, "netwasm.entry.callback");
  const exitCode = requireInstanceFunction(instance.exports, "netwasm.entry.exit_code");
  return Object.freeze({
    contractKey: plan.contractKey,
    command: Object.freeze({
      async run() {
        await componentModel.drive(invoke(), callback);
        return exitCode();
      },
    }),
  });
}

function validatePlan(plan) {
  assertExactObject(plan, planKeys, "raw ABI plan");
  if (plan.contractKey !== commandExecutionContract
      || plan.entryPoint === null || typeof plan.entryPoint !== "object"
      || !Array.isArray(plan.invocationArguments)) {
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
