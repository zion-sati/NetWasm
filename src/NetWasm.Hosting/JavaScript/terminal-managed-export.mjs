import { NetWasmManagedError } from "./managed-errors.mjs";

// Adapter: invoke an export using the runtime's terminal-event contract.
export function createTerminalManagedExport(name, invoke, consumeTerminalEvent, clearActiveException) {
  if (typeof name !== "string" || name.length === 0)
    throw new TypeError("An export name is required");
  if (typeof invoke !== "function" || typeof consumeTerminalEvent !== "function" ||
      typeof clearActiveException !== "function")
    throw new TypeError("Export invocation, terminal-event consumption and exception cleanup are required");
  return (...args) => {
    try {
      return invoke(...args);
    } catch (cause) {
      if (!(cause instanceof WebAssembly.RuntimeError)) throw cause;
      const event = consumeTerminalEvent();
      if (event == null) throw cause;
      if (!Number.isInteger(event.typeId) || event.typeId <= 0 || event.typeId > 0x7fffffff)
        throw new TypeError('Terminal events require a positive managed type ID');
      clearActiveException();
      throw new NetWasmManagedError(name, { cause, managedType: event.typeId });
    }
  };
}
