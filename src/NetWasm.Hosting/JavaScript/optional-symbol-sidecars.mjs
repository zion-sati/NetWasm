import { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";

// The symbols sidecars are optional (issue 225): a program and its host keep working without them.
// A type-symbols or method-symbols file that cannot be read, fails its integrity check or does not
// parse is left out, so names read type#<id> and frames method#<id>; only cancellation propagates.
export async function readOptionalSymbolSidecar(artifact, verify, signal) {
  if (artifact === undefined) {
    return undefined;
  }
  try {
    return await verify(artifact);
  } catch (error) {
    if (signal?.aborted || error?.name === "AbortError") {
      throw error;
    }
    return undefined;
  }
}

// The frames a method-symbols sidecar names; none when it is absent or malformed.
export function readOptionalStackTraceSymbols(bytes) {
  if (bytes === undefined) {
    return [];
  }
  try {
    return parseStackTraceSymbols(bytes);
  } catch {
    return [];
  }
}
