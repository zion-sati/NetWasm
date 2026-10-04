import { parseInteropManifest } from "./interop-manifest-reader.mjs";
import { buildRawModuleInventoryPlan } from "./raw-module-inventory-plan-builder.mjs";

const executableExportNames = Object.freeze([
  "run",
  "netwasm.process.status",
  "netwasm.process.result",
  "netwasm.process.complete",
]);

export function buildRawLibraryAbiPlan(request = {}) {
  assertExactObject(request, ["abi", "manifest"], "raw library ABI planning request");
  const { abi, manifest } = request;
  const inventory = buildRawModuleInventoryPlan({ abi });
  const contract = parseInteropManifest(manifest);
  if (abi.entryPoint !== null) {
    throw new TypeError("raw library cannot contain an executable entry point");
  }
  if (contract.target !== inventory.target) {
    throw new TypeError("raw library interop target does not match the final ABI");
  }

  const canonicalMemoryExport = `${inventory.prefix}_memory`;
  const canonicalReallocateExport = `${inventory.prefix}_realloc`;
  const canonicalInitializeExport = `${inventory.prefix}_initialize`;
  requireExport(inventory.exports, canonicalMemoryExport, "memory");
  requireExport(inventory.exports, canonicalReallocateExport, "function");
  requireExport(inventory.exports, canonicalInitializeExport, "function");

  const applicationExportNames = new Set();
  const managedExportNames = [];
  for (const descriptor of contract.exports) {
    applicationExportNames.add(descriptor.name);
    requireManagedExport(inventory.exports, managedExportNames, descriptor.name);
    if (descriptor.asyncReturn !== null && descriptor.asyncReturn !== undefined) {
      requireManagedExport(inventory.exports, managedExportNames, descriptor.statusExport);
      requireManagedExport(inventory.exports, managedExportNames, descriptor.completeExport);
      if (descriptor.result !== "void") {
        requireManagedExport(inventory.exports, managedExportNames, descriptor.resultExport);
      }
    }
  }
  for (const callback of contract.callbacks ?? []) {
    requireManagedExport(inventory.exports, managedExportNames, callback.exportName);
  }
  for (const descriptor of contract.imports) {
    if (descriptor.asyncReturn === null || descriptor.asyncReturn === undefined) continue;
    requireManagedExport(inventory.exports, managedExportNames, descriptor.resolveExport);
    requireManagedExport(inventory.exports, managedExportNames, descriptor.rejectExport);
    requireManagedExport(inventory.exports, managedExportNames, descriptor.cancelExport);
  }

  for (const name of executableExportNames) {
    if (inventory.exports.some(descriptor => descriptor.name === name)
        && !applicationExportNames.has(name)) {
      throw new TypeError(`raw library contains executable export '${name}'`);
    }
  }

  return Object.freeze({
    target: inventory.target,
    imports: inventory.imports,
    exports: inventory.exports,
    reactorHostModule: inventory.reactorHostModule,
    reactorGuestExport: inventory.reactorGuestExport,
    canonicalMemoryExport,
    canonicalReallocateExport,
    canonicalInitializeExport,
    managedExportNames: Object.freeze(managedExportNames),
  });
}

function requireManagedExport(exports, names, name) {
  requireExport(exports, name, "function");
  if (!names.includes(name)) names.push(name);
}

function requireExport(exports, name, kind) {
  const descriptor = exports.find(candidate => candidate.name === name);
  if (descriptor?.kind !== kind) {
    throw new TypeError(`raw ${kind} export '${name}' is required`);
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
