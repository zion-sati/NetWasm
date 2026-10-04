import {
  commandExecutionContract,
  processExecutionContract,
} from "./execution-contracts.mjs";
import { buildRawModuleInventoryPlan } from "./raw-module-inventory-plan-builder.mjs";

const entryPointKeys = ["completionShape", "parameterShape", "returnShape"];
const processExports = Object.freeze([
  "netwasm.process.status",
  "netwasm.process.result",
  "netwasm.process.complete",
]);

export function buildRawAbiPlan(request = {}) {
  assertExactObject(request, ["abi", "contractKey"], "raw ABI planning request");
  const { contractKey, abi } = request;
  if (contractKey !== commandExecutionContract && contractKey !== processExecutionContract) {
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
  const { imports, exports, reactorHostModule, reactorGuestExport } = inventory;
  validateExecutionShape(
    contractKey,
    entryPoint,
    imports,
    exports,
    reactorHostModule,
    reactorGuestExport);
  const invocationArguments = entryPoint.parameterShape === "none"
    ? Object.freeze([])
    : Object.freeze([inventory.zero]);

  return Object.freeze({
    contractKey,
    target: abi.target,
    entryPoint,
    imports,
    exports,
    reactorHostModule,
    reactorGuestExport,
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

function validateExecutionShape(
  contractKey,
  entryPoint,
  imports,
  exports,
  reactorHostModule,
  reactorGuestExport) {
  requireFunctionExport(exports, "run");
  const exportedNames = new Set(exports.map(descriptor => descriptor.name));
  const reactorImports = imports.filter(descriptor => descriptor.module === reactorHostModule);
  for (const descriptor of reactorImports) {
    if (descriptor.kind !== "function"
        || descriptor.name !== "watch" && descriptor.name !== "cancel") {
      throw new TypeError("raw reactor-host import is invalid");
    }
  }

  if (contractKey === commandExecutionContract) {
    if (entryPoint.completionShape !== "synchronous") {
      throw new TypeError("raw command requires synchronous completion");
    }
    if (reactorImports.length !== 0
        || processExports.some(name => exportedNames.has(name))
        || [...exportedNames].some(name => name.endsWith("|netwasm:runtime/reactor-guest@1|wake"))) {
      throw new TypeError("raw command mixes process ABI members");
    }
    return;
  }

  if (entryPoint.completionShape !== "asynchronous") {
    throw new TypeError("raw process requires asynchronous completion");
  }
  requireFunctionExport(exports, "netwasm.process.status");
  requireFunctionExport(exports, "netwasm.process.complete");
  requireFunctionExport(exports, reactorGuestExport);
  const hasResult = exportedNames.has("netwasm.process.result");
  if (hasResult !== (entryPoint.returnShape === "exitCode")) {
    throw new TypeError("raw process result export does not match its return shape");
  }
  if (hasResult) requireFunctionExport(exports, "netwasm.process.result");
  for (const name of exportedNames) {
    if (name.endsWith("|netwasm:runtime/reactor-guest@1|wake")
        && name !== reactorGuestExport) {
      throw new TypeError("raw process contains a wrong-target reactor guest export");
    }
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
