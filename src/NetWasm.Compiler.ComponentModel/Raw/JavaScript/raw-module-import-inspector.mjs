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
  const identities = new Map();
  const identityOrder = [];
  for (const entry of inventory) {
    if (entry.kind !== "function") {
      throw new RawModuleInspectionError("unsupported-import-kind");
    }
    const identity = JSON.stringify([entry.module, entry.name]);
    if (!identities.has(identity)) {
      identities.set(identity, 0);
      identityOrder.push(entry);
    }
    identities.set(identity, identities.get(identity) + 1);
  }

  const signatures = await readers.readFunctionImports(new Uint8Array(snapshot));
  const byIdentity = new Map();
  for (const entry of signatures) {
    const identity = JSON.stringify([entry.module, entry.name]);
    if (!identities.has(identity)) {
      throw new RawModuleInspectionError("inconsistent-import-inventory");
    }
    const matches = byIdentity.get(identity) ?? [];
    matches.push(entry);
    byIdentity.set(identity, matches);
  }
  if (byIdentity.size !== identities.size) {
    throw new RawModuleInspectionError("inconsistent-import-inventory");
  }

  return Object.freeze(identityOrder.map(entry => {
    const identity = JSON.stringify([entry.module, entry.name]);
    const matches = byIdentity.get(identity);
    if (matches.length !== identities.get(identity)
        || !matches.every(candidate =>
          sameTypes(candidate.parameters, matches[0].parameters)
          && sameTypes(candidate.results, matches[0].results))) {
      throw new RawModuleInspectionError("inconsistent-import-inventory");
    }
    const signature = matches[0];
    return Object.freeze({
      module: entry.module,
      name: entry.name,
      parameters: Object.freeze([...signature.parameters]),
      results: Object.freeze([...signature.results]),
    });
  }));
}

function sameTypes(left, right) {
  return left.length === right.length
    && left.every((value, index) => value === right[index]);
}
