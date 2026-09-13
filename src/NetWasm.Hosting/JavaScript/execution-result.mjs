const completionKinds = new Set([
  "normal",
  "managedFailure",
  "managedCancellation",
  "callerCancellation",
  "contractFailure",
  "hostFailure",
]);
const failurePhases = new Set([
  "validation",
  "instantiation",
  "execution",
  "observation",
  "cleanup",
  "output",
]);
const cleanupPhases = new Set(["cleanup", "output"]);
const failureCodePattern = /^[a-z][a-z0-9.-]*$/;
const controlCharacterPattern = /\p{Cc}/u;
const minSignedInt32 = -0x80000000;
const maxSignedInt32 = 0x7fffffff;

export function normalExecutionResult(exitCode) {
  return executionResult("normal", exitCode, null, []);
}

export function failedExecutionResult(completionKind, phase, code, message) {
  return executionResult(
    completionKind,
    null,
    executionFailure(phase, code, message),
    []);
}

export function executionFailure(phase, code, message) {
  const result = { phase, code, message };
  assertFailure(result);
  return Object.freeze(result);
}

export function appendCleanupFailures(outcome, cleanupFailures) {
  assertExecutionResult(outcome);
  if (!Array.isArray(cleanupFailures)) {
    throw new TypeError("cleanup failures must be an array");
  }
  for (const item of cleanupFailures) {
    assertFailure(item);
    if (!cleanupPhases.has(item.phase)) {
      throw new TypeError("cleanup failures require cleanup or output phase");
    }
  }
  if (cleanupFailures.length === 0) return outcome;
  if (outcome.completionKind === "normal") {
    return executionResult(
      "hostFailure",
      null,
      cleanupFailures[0],
      cleanupFailures.slice(1));
  }
  return executionResult(
    outcome.completionKind,
    null,
    outcome.primaryFailure,
    [...outcome.cleanupFailures, ...cleanupFailures]);
}

export function assertExecutionResult(result) {
  if (result === null || typeof result !== "object") {
    throw new TypeError("execution result is required");
  }
  if (result.schemaVersion !== 1) {
    throw new TypeError("execution result schema is unsupported");
  }
  if (!completionKinds.has(result.completionKind)) {
    throw new TypeError("execution completion kind is unsupported");
  }
  if (!Array.isArray(result.cleanupFailures)) {
    throw new TypeError("execution cleanup failures must be an array");
  }
  for (const item of result.cleanupFailures) {
    assertFailure(item);
    if (!cleanupPhases.has(item.phase)) {
      throw new TypeError("execution cleanup failure phase is invalid");
    }
  }
  if (result.completionKind === "normal") {
    if (!Number.isInteger(result.exitCode)
        || result.exitCode < minSignedInt32
        || result.exitCode > maxSignedInt32
        || result.primaryFailure !== null
        || result.cleanupFailures.length !== 0) {
      throw new TypeError("normal execution result is invalid");
    }
    return;
  }
  if (result.exitCode !== null || result.primaryFailure === null) {
    throw new TypeError("failed execution result is invalid");
  }
  assertFailure(result.primaryFailure);
}

function executionResult(completionKind, exitCode, primaryFailure, cleanupFailures) {
  const result = {
    schemaVersion: 1,
    completionKind,
    exitCode,
    primaryFailure,
    cleanupFailures: Object.freeze([...cleanupFailures]),
  };
  assertExecutionResult(result);
  return Object.freeze(result);
}

function assertFailure(item) {
  if (item === null || typeof item !== "object" || !failurePhases.has(item.phase)) {
    throw new TypeError("execution failure is invalid");
  }
  if (typeof item.code !== "string" || !failureCodePattern.test(item.code)) {
    throw new TypeError("execution failure code is invalid");
  }
  if (typeof item.message !== "string"
      || item.message.length === 0
      || item.message.trim() !== item.message
      || controlCharacterPattern.test(item.message)) {
    throw new TypeError("execution failure message is invalid");
  }
}
