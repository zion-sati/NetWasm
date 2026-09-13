import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

export async function inspectRawModuleImports(bytes, readers) {
  if (!(bytes instanceof Uint8Array)) {
    throw new RawModuleInspectionError("invalid-bytes");
  }
  if (typeof readers?.readCoreImports !== "function"
      || typeof readers.readFunctionImports !== "function") {
    throw new TypeError("Core and function import readers are required.");
  }
  const snapshot = new Uint8Array(bytes);
  const inventory = await readers.readCoreImports(new Uint8Array(snapshot));
  const identities = new Set();
  for (const entry of inventory) {
    if (entry.kind !== "function") {
      throw new RawModuleInspectionError("unsupported-import-kind");
    }
    const identity = JSON.stringify([entry.module, entry.name]);
    if (identities.has(identity)) {
      throw new RawModuleInspectionError("duplicate-import");
    }
    identities.add(identity);
  }

  const signatures = await readers.readFunctionImports(new Uint8Array(snapshot));
  const byIdentity = new Map();
  for (const entry of signatures) {
    const identity = JSON.stringify([entry.module, entry.name]);
    if (!identities.has(identity) || byIdentity.has(identity)) {
      throw new RawModuleInspectionError("inconsistent-import-inventory");
    }
    byIdentity.set(identity, entry);
  }
  if (byIdentity.size !== identities.size) {
    throw new RawModuleInspectionError("inconsistent-import-inventory");
  }

  return Object.freeze(inventory.map(entry => {
    const signature = byIdentity.get(JSON.stringify([entry.module, entry.name]));
    return Object.freeze({
      module: entry.module,
      name: entry.name,
      parameters: Object.freeze([...signature.parameters]),
      results: Object.freeze([...signature.results]),
    });
  }));
}
