// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { unwrapContractError } from "./contract-error.mjs";

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
const controlCharacterRuns = /\p{Cc}+/gu;
const whitespaceRuns = /\s+/gu;
const maxDetailLength = 1024;
const minSignedInt32 = -0x80000000;
const maxSignedInt32 = 0x7fffffff;

export function normalExecutionResult(exitCode) {
  return executionResult("normal", exitCode, null, []);
}

export function failedExecutionResult(completionKind, phase, code, message, cause) {
  return executionResult(
    completionKind,
    null,
    executionFailure(phase, code, message, cause),
    []);
}

// A cause is the value a host caught. It contributes a single-line `detail`;
// the stable code and message stay fixed for programmatic handling.
export function executionFailure(phase, code, message, cause) {
  return failure(phase, code, message,
    cause === undefined ? undefined : describeFailureCause(cause));
}

export function describeFailureCause(cause) {
  return normalizeDetail(causeText(unwrapContractError(cause)));
}

export function isFailureDetail(value) {
  return typeof value === "string"
    && value.length !== 0
    && value.length <= maxDetailLength
    && value.trim() === value
    && !controlCharacterPattern.test(value);
}

// Hosts that must not disclose local paths rewrite each detail at their boundary.
export function mapFailureDetails(result, transform) {
  assertExecutionResult(result);
  if (typeof transform !== "function") {
    throw new TypeError("failure detail transform is required");
  }
  if (result.completionKind === "normal") return result;
  const map = item => "detail" in item
    ? failure(item.phase, item.code, item.message, normalizeDetail(transform(item.detail)))
    : item;
  return executionResult(
    result.completionKind,
    null,
    map(result.primaryFailure),
    result.cleanupFailures.map(map));
}

function failure(phase, code, message, detail) {
  const result = detail === undefined
    ? { phase, code, message }
    : { phase, code, message, detail };
  assertFailure(result);
  return Object.freeze(result);
}

function causeText(cause) {
  try {
    if (cause !== null && typeof cause === "object" && typeof cause.message === "string") {
      const name = typeof cause.name === "string" && cause.name.length !== 0 ? cause.name : "Error";
      return cause.message.length === 0 ? name : `${name}: ${cause.message}`;
    }
    return String(cause);
  } catch {
    return "";
  }
}

function normalizeDetail(text) {
  if (typeof text !== "string") {
    throw new TypeError("failure detail transform must return text");
  }
  const line = text.replace(controlCharacterRuns, " ").replace(whitespaceRuns, " ").trim();
  if (line.length === 0) return undefined;
  return line.length <= maxDetailLength
    ? line
    : `${line.slice(0, maxDetailLength - 1).trimEnd()}\u2026`;
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
  if ("detail" in item && !isFailureDetail(item.detail)) {
    throw new TypeError("execution failure detail is invalid");
  }
}
