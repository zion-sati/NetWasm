import {
  failedExecutionResult,
  normalExecutionResult,
} from "./execution-result.mjs";

const minSignedInt32 = -0x80000000;
const maxSignedInt32 = 0x7fffffff;

export function executeCommand(request = {}) {
  if (request === null || typeof request !== "object") {
    throw new TypeError("command execution request is required");
  }
  const { run, signal = null } = request;
  if (typeof run !== "function") {
    throw new TypeError("canonical command run action is required");
  }
  if (signal !== null && (typeof signal.aborted !== "boolean"
      || typeof signal.addEventListener !== "function"
      || typeof signal.removeEventListener !== "function")) {
    throw new TypeError("command signal must be an AbortSignal");
  }
  if (signal?.aborted) {
    return failedExecutionResult(
      "callerCancellation",
      "execution",
      "caller.cancelled",
      "Command execution was cancelled by the caller before guest entry.");
  }

  let exitCode;
  try {
    exitCode = run();
  } catch {
    return failedExecutionResult(
      "hostFailure",
      "execution",
      "host.command-invoke",
      "The execution host could not invoke the canonical command.");
  }
  if (!Number.isInteger(exitCode)
      || exitCode < minSignedInt32
      || exitCode > maxSignedInt32) {
    return failedExecutionResult(
      "contractFailure",
      "execution",
      "contract.command-exit-code",
      "The canonical command did not return a signed 32-bit exit code.");
  }
  return normalExecutionResult(exitCode);
}
