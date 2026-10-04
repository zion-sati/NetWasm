import { readManagedExceptionDetails } from "./managed-exception-details.mjs";
import {
  copyManagedString, readStackTraceSymbolNames, resolveStackTrace,
} from "./managed-exception-reporter.mjs";

export function createManagedExceptionCapture({
  getMemory, stringDataOffset, acquireHandle, stackTraceSymbols,
}) {
  if (typeof getMemory !== "function" || typeof acquireHandle !== "function"
      || !Number.isSafeInteger(stringDataOffset) || stringDataOffset < 0) {
    throw new TypeError("managed exception capture actions are invalid");
  }
  const symbols = readStackTraceSymbolNames(stackTraceSymbols);
  return Object.freeze(function capture(typeId, messageReference, messageLength, traceReference, traceLength) {
    const memory = getMemory();
    // Copy while the generated completion still roots the managed exception.
    // The host handle owns strings only, never a borrowed Wasm pointer.
    const details = readManagedExceptionDetails({
      typeId, typeName: null,
      message: copyManagedString(memory, messageReference, messageLength, stringDataOffset),
      stackTrace: resolveStackTrace(
        copyManagedString(memory, traceReference, traceLength, stringDataOffset), symbols),
    });
    return acquireHandle(details);
  });
}
