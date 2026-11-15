// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import {
  appendCleanupFailures,
  assertExecutionResult,
  executionFailure,
} from "./execution-result.mjs";

export function createExecutionScopeCloser(releaseActions = []) {
  if (!Array.isArray(releaseActions)) {
    throw new TypeError("execution release actions must be an array");
  }

  const actions = releaseActions.map(action => {
    if (action === null || typeof action !== "object"
        || typeof action.release !== "function") {
      throw new TypeError("execution release action is invalid");
    }
    const phase = action.phase ?? "cleanup";
    const failure = executionFailure(phase, action.code, action.message);
    if (failure.phase !== "cleanup" && failure.phase !== "output") {
      throw new TypeError("execution release action phase must be cleanup or output");
    }
    return Object.freeze({
      release: action.release,
      fail: cause => executionFailure(phase, action.code, action.message, cause),
    });
  });

  let closed = false;
  return Object.freeze(async outcome => {
    assertExecutionResult(outcome);
    if (closed) {
      throw new Error("execution scope is already closed");
    }
    closed = true;

    const cleanupFailures = [];
    for (const action of actions) {
      try {
        await action.release();
      } catch (error) {
        cleanupFailures.push(action.fail(error));
      }
    }
    return appendCleanupFailures(outcome, cleanupFailures);
  });
}

export async function closeExecutionScope(outcome, releaseActions = []) {
  assertExecutionResult(outcome);
  return createExecutionScopeCloser(releaseActions)(outcome);
}
