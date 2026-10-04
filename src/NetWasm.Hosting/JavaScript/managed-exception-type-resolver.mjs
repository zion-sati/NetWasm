import { loadAndValidateArtifacts } from "./managed-exception-reporter.mjs";

export function createManagedExceptionTypeResolver({ loadArtifacts, crypto }) {
  if (typeof loadArtifacts !== "function") {
    throw new TypeError("managed exception artifact loader is required");
  }
  let artifacts;
  return Object.freeze(async function resolve(typeId) {
    if (!Number.isSafeInteger(typeId) || typeId <= 0 || typeId > 0x7fffffff) {
      throw new TypeError("managed exception type ID is invalid");
    }
    artifacts ??= loadAndValidateArtifacts(loadArtifacts, crypto).catch(() => null);
    const loaded = await artifacts;
    // Optional/missing diagnostic artifacts must not erase the original payload.
    return loaded?.types.get(typeId)?.displayName ?? null;
  });
}
