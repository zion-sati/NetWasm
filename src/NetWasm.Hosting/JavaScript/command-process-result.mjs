// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { assertExecutionResult } from "./execution-result.mjs";

export function adaptCommandProcessResult(result) {
  assertExecutionResult(result);
  if (result.completionKind === "normal") {
    return Object.freeze({
      diagnostic: null,
      exitCode: normalizeExitCode(result.exitCode),
    });
  }
  const failure = result.primaryFailure;
  const detail = "detail" in failure ? ` Detail: ${failure.detail}` : "";
  return Object.freeze({
    diagnostic: `NetWasm ${result.completionKind}: ${failure.code}: ${failure.message}${detail}\n`,
    exitCode: 1,
  });
}

function normalizeExitCode(exitCode) {
  if (exitCode === 0) return 0;
  return exitCode > 0 && exitCode <= 255 ? exitCode : 1;
}
