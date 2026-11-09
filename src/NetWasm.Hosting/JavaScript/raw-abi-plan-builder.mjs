import { commandExecutionContract } from "./execution-contracts.mjs";
import { buildRawModuleInventoryPlan } from "./raw-module-inventory-plan-builder.mjs";

const entryPointKeys = ["completionShape", "parameterShape", "returnShape"];
// The exports through which the host drives an asynchronous Main.
const asynchronousEntryExports = Object.freeze(["netwasm.entry.callback", "netwasm.entry.exit_code"]);

export function buildRawAbiPlan(request = {}) {
  assertExactObject(request, ["abi", "contractKey"], "raw ABI planning request");
  const { contractKey, abi } = request;
  if (contractKey !== commandExecutionContract) {
    throw new TypeError("raw execution contract is unsupported");
  }
  const inventory = buildRawModuleInventoryPlan({ abi });
  assertExactObject(abi.entryPoint, entryPointKeys, "raw entry point");
  const entryPoint = Object.freeze({
    parameterShape: abi.entryPoint.parameterShape,
    returnShape: abi.entryPoint.returnShape,
    completionShape: abi.entryPoint.completionShape,
  });
  validateEntryPoint(entryPoint);
  const { imports, exports } = inventory;
  validateExecutionShape(entryPoint, exports);
  const invocationArguments = entryPoint.parameterShape === "none"
    ? Object.freeze([])
    : Object.freeze([inventory.zero]);

  return Object.freeze({
    contractKey,
    target: abi.target,
    entryPoint,
    imports,
    exports,
    invocationArguments,
  });
}

function validateEntryPoint(entryPoint) {
  if (entryPoint.parameterShape !== "none"
      && entryPoint.parameterShape !== "stringArray") {
    throw new TypeError("raw entry-point parameter shape is unsupported");
  }
  if (entryPoint.returnShape !== "void"
      && entryPoint.returnShape !== "exitCode") {
    throw new TypeError("raw entry-point return shape is unsupported");
  }
  if (entryPoint.completionShape !== "synchronous"
      && entryPoint.completionShape !== "asynchronous") {
    throw new TypeError("raw entry-point completion shape is unsupported");
  }
}

function validateExecutionShape(entryPoint, exports) {
  requireFunctionExport(exports, "run");
  const exportedNames = new Set(exports.map(descriptor => descriptor.name));
  if (entryPoint.completionShape === "asynchronous") {
    for (const name of asynchronousEntryExports) requireFunctionExport(exports, name);
  } else if (asynchronousEntryExports.some(name => exportedNames.has(name))) {
    throw new TypeError("raw synchronous command mixes asynchronous entry exports");
  }
}
function requireFunctionExport(exports, name) {
  const descriptor = exports.find(candidate => candidate.name === name);
  if (descriptor?.kind !== "function") {
    throw new TypeError(`raw function export '${name}' is required`);
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
      || Object.values(descriptors).some(descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}
