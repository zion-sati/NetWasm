// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import { isAbsolute } from "node:path";
import test from "node:test";

import { failedExecutionResult, normalExecutionResult } from "./execution-result.mjs";
import { createFailureDetailRedaction } from "./failure-detail-redaction.mjs";

const redact = createFailureDetailRedaction({ isAbsoluteHostPath: isAbsolute });
const failed = cause => failedExecutionResult(
  "hostFailure", "instantiation", "host.raw-load", "The host failed.", cause);

test("replaces quoted and bare local paths and file URLs", () => {
  const result = redact(failed(new Error(
    "ENOENT: no such file or directory, open '/srv/some one/app.wasm' "
      + "from /opt/app/host.mjs: and file:///opt/app/x.mjs.")));

  assert.equal(result.primaryFailure.detail,
    "Error: ENOENT: no such file or directory, open '<host-path>' "
      + "from <host-path>: and <host-path>.");
  assert.equal(Object.isFrozen(result.primaryFailure), true);
});

test("keeps web URLs, relative paths, and separators", () => {
  const detail = "Error: fetch https://example.com/app.wasm, ./app.wasm, a / b and 'quoted text'";

  assert.equal(redact(failed(new Error(detail.slice(7)))).primaryFailure.detail, detail);
});

test("redacts cleanup failures and leaves failures without detail", () => {
  const outcome = redact(Object.freeze({
    schemaVersion: 1,
    completionKind: "hostFailure",
    exitCode: null,
    primaryFailure: Object.freeze({ phase: "execution", code: "host.failure", message: "Failed." }),
    cleanupFailures: Object.freeze([Object.freeze({
      phase: "cleanup",
      code: "host.release",
      message: "Release failed.",
      detail: "Error: rm /tmp/x/y",
    })]),
  }));

  assert.equal("detail" in outcome.primaryFailure, false);
  assert.equal(outcome.cleanupFailures[0].detail, "Error: rm <host-path>");
});

test("returns normal results unchanged and uses the host's path test", () => {
  const normal = normalExecutionResult(0);
  const browser = createFailureDetailRedaction({ isAbsoluteHostPath: () => false });

  assert.equal(redact(normal), normal);
  assert.equal(browser(failed(new Error("/app/app.wasm"))).primaryFailure.detail,
    "Error: /app/app.wasm");
  assert.throws(() => createFailureDetailRedaction({}), /absolute host path test/);
});
