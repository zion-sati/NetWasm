import {
  createManagedExceptionEventReporter,
} from "./managed-exception-reporter.mjs";

export const componentDiagnosticsModule = "netwasm:diagnostics/terminal";

export function createComponentManagedExceptionHost({
  diagnosticArtifacts,
  managedExceptionReporting = {},
  stackTraceSymbols = [],
}) {
  const reporter = createManagedExceptionEventReporter({
    maximumReportedMessageLength:
      managedExceptionReporting.maximumMessageLength ?? Number.POSITIVE_INFINITY,
    reportImmediate: managedExceptionReporting.reportImmediate ?? (() => {}),
    reportEnriched: managedExceptionReporting.reportEnriched ?? (() => {}),
    loadArtifacts: async () => {
      if (typeof diagnosticArtifacts === "function") return diagnosticArtifacts();
      if (diagnosticArtifacts != null) return diagnosticArtifacts;
      throw new Error("diagnostic exception artifacts were not deployed");
    },
    stackTraceSymbols,
  });
  return Object.freeze({
    imports: Object.freeze({
      report(typeId, message, stackTrace) {
        reporter.reportTerminalEvent(
          typeId,
          decodeText(message, "message"),
          decodeText(stackTrace, "stack trace"));
      },
    }),
    consumeTerminalEvent: reporter.consumeTerminalEvent,
    drain: reporter.drain,
  });
}

function decodeText(value, name) {
  // Jco lifts WIT option::none to undefined. Normalize it to the reporter's
  // null representation, while retaining a present, empty UTF-16 list.
  if (value === undefined || value === null) return null;
  if (!(value instanceof Uint16Array) && !Array.isArray(value)) {
    throw new TypeError(`component managed exception ${name} is invalid`);
  }
  let message = "";
  for (const unit of value) {
    if (!Number.isInteger(unit) || unit < 0 || unit > 0xffff) {
      throw new TypeError(`component managed exception ${name} is invalid`);
    }
    message += String.fromCharCode(unit);
  }
  return message;
}
