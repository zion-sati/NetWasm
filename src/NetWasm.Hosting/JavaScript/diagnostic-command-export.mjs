import { NetWasmManagedError } from "./managed-errors.mjs";

// Adapter: execute the typed command contract without a post-trap guest call.
export function createDiagnosticCommandExport(invoke, report) {
  if (typeof invoke !== "function" || typeof report !== "function") {
    throw new TypeError("Command invocation and diagnostic reporting are required");
  }
  return () => {
    const completion = invoke();
    if (completion === null || typeof completion !== "object"
        || Array.isArray(completion)) {
      throw new TypeError("The diagnostic command completion is invalid");
    }
    if (completion.tag === "exited") return completion.val;
    if (completion.tag !== "failed") {
      throw new TypeError("The diagnostic command completion case is invalid");
    }
    const event = completion.val;
    if (event === null || typeof event !== "object" || Array.isArray(event)
        || !Object.hasOwn(event, "typeId") || !Object.hasOwn(event, "message")
        || !Object.hasOwn(event, "stackTrace")) {
      throw new TypeError("The diagnostic command terminal event is invalid");
    }
    // Jco represents an absent WIT option as undefined. The reporter's owned
    // event contract uses null and preserves a present, empty UTF-16 list.
    report(event.typeId, event.message ?? null, event.stackTrace ?? null);
    throw new NetWasmManagedError("run", { managedType: event.typeId });
  };
}
