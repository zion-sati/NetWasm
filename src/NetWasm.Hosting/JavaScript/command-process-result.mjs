import { assertExecutionResult } from "./execution-result.mjs";

export function adaptCommandProcessResult(result) {
  assertExecutionResult(result);
  if (result.completionKind === "normal") {
    return Object.freeze({
      diagnostic: null,
      exitCode: normalizeExitCode(result.exitCode),
    });
  }
  return Object.freeze({
    diagnostic: `NetWasm ${result.completionKind}: ${result.primaryFailure.code}: ${result.primaryFailure.message}\n`,
    exitCode: 1,
  });
}

function normalizeExitCode(exitCode) {
  if (exitCode === 0) return 0;
  return exitCode > 0 && exitCode <= 255 ? exitCode : 1;
}
