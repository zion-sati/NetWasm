import { readManagedExceptionDetails } from "./managed-exception-details.mjs";
import { normalizeInteropHandle } from "./interop-handle-table.mjs";

export function createManagedExceptionPayloadConsumer({ getHandle, releaseHandle, resolveType }) {
  if ([getHandle, releaseHandle, resolveType].some(action => typeof action !== "function")) {
    throw new TypeError("managed exception payload consumer actions are invalid");
  }
  return Object.freeze(async function consume(handle) {
    const normalized = normalizeInteropHandle(handle);
    if (normalized === 0) return null;
    let details;
    try {
      details = readManagedExceptionDetails(getHandle(normalized));
    } finally {
      // Release synchronously, before asynchronous symbol resolution. Every
      // invocation retains its own copied details across concurrent lookups.
      releaseHandle(normalized);
    }
    const typeName = await resolveType(details.typeId);
    return readManagedExceptionDetails({ ...details, typeName });
  });
}
