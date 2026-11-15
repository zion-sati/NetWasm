// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { adaptCommandProcessResult } from "./command-process-result.mjs";
import { failedExecutionResult, normalExecutionResult } from "./execution-result.mjs";

test("preserves portable guest exit codes for the command process", () => {
  assert.deepEqual(adaptCommandProcessResult(normalExecutionResult(0)), {
    diagnostic: null,
    exitCode: 0,
  });
  assert.deepEqual(adaptCommandProcessResult(normalExecutionResult(17)), {
    diagnostic: null,
    exitCode: 17,
  });
});

test("normalizes nonportable guest exit codes to command failure", () => {
  for (const exitCode of [-1, 256, 0x7fffffff]) {
    assert.deepEqual(adaptCommandProcessResult(normalExecutionResult(exitCode)), {
      diagnostic: null,
      exitCode: 1,
    });
  }
});

test("turns structured failures into visible command failures", () => {
  const adapted = adaptCommandProcessResult(failedExecutionResult(
    "contractFailure",
    "execution",
    "contract.entry",
    "The entry point is incompatible."));
  assert.deepEqual(adapted, {
    diagnostic: "NetWasm contractFailure: contract.entry: The entry point is incompatible.\n",
    exitCode: 1,
  });
});

test("appends the failure detail to the command diagnostic", () => {
  const adapted = adaptCommandProcessResult(failedExecutionResult(
    "contractFailure",
    "validation",
    "contract.invalid",
    "Execution contract validation failed.",
    new TypeError("platform capability 'clocks' was not granted")));
  assert.equal(adapted.diagnostic,
    "NetWasm contractFailure: contract.invalid: Execution contract validation failed. "
      + "Detail: TypeError: platform capability 'clocks' was not granted\n");
});

test("rejects malformed structured results", () => {
  assert.throws(() => adaptCommandProcessResult({}), /schema/i);
});
