import { isAbsolute } from "node:path";
import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

const schemaVersion = "1";
const kind = "raw-module-import-signatures";
const coreTypes = new Set(["i32", "i64", "f32", "f64"]);
const inspectionErrorCodes = new Set([
  "invalid-bytes",
  "invalid-core-module",
  "unsupported-import-kind",
  "unsupported-import-signature",
  "duplicate-import",
  "decoder-failure",
  "inconsistent-import-inventory",
]);

export async function runRawModuleInspectionCommand(commandArguments, dependencies) {
  requireDependencies(dependencies);

  let envelope;
  let exitCode;
  if (!hasValidArguments(commandArguments)) {
    envelope = failure("invalid-arguments");
    exitCode = 1;
  } else {
    try {
      const [modulePath, binaryenPath] = commandArguments;
      const bytes = await dependencies.readModule(modulePath);
      const decoder = await dependencies.loadDecoder(binaryenPath);
      const imports = await dependencies.inspectModule(bytes, decoder);
      envelope = success(snapshotImports(imports));
      exitCode = 0;
    } catch (error) {
      envelope = failure(error instanceof RawModuleInspectionError
        && inspectionErrorCodes.has(error.code)
        ? error.code
        : "inspection-command-failure");
      exitCode = 1;
    }
  }

  await dependencies.writeOutput(`${JSON.stringify(envelope)}\n`);
  return exitCode;
}

function requireDependencies(dependencies) {
  if (typeof dependencies?.readModule !== "function"
      || typeof dependencies.loadDecoder !== "function"
      || typeof dependencies.inspectModule !== "function"
      || typeof dependencies.writeOutput !== "function") {
    throw new TypeError("Raw module inspection command dependencies are required.");
  }
}

function hasValidArguments(commandArguments) {
  return Array.isArray(commandArguments)
    && commandArguments.length === 2
    && commandArguments.every(value => typeof value === "string"
      && value.length > 0 && isAbsolute(value));
}

function snapshotImports(imports) {
  if (!Array.isArray(imports)) {
    throw new TypeError("Raw module inspection must return an import array.");
  }
  return imports.map(entry => {
    if (entry === null || typeof entry !== "object"
        || typeof entry.module !== "string" || typeof entry.name !== "string"
        || !isCoreTypeArray(entry.parameters) || !isCoreTypeArray(entry.results)) {
      throw new TypeError("Raw module inspection returned an invalid import.");
    }
    return {
      module: entry.module,
      name: entry.name,
      parameters: [...entry.parameters],
      results: [...entry.results],
    };
  });
}

function isCoreTypeArray(value) {
  return Array.isArray(value) && value.every(type => coreTypes.has(type));
}

function success(imports) {
  return { schemaVersion, kind, imports };
}

function failure(errorCode) {
  return { schemaVersion, kind, errorCode };
}
