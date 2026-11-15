// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createExportDepthGuard, maximumExportDepth } from "./export-depth-guard.mjs";
import { NetWasmHostError } from "./managed-errors.mjs";

test("allows nested export entry up to the limit and recovers after leaving", () => {
  assert.equal(maximumExportDepth, 32);
  const enter = createExportDepthGuard(2);
  const outer = enter();
  const inner = enter();
  assert.throws(enter, error => error instanceof NetWasmHostError
    && error.message === "NetWasm export calls cannot nest more than 2 levels deep");
  inner();
  inner();
  const again = enter();
  again();
  outer();
  const levels = Array.from({ length: maximumExportDepth }, createExportDepthGuard());
  assert.equal(levels.length, maximumExportDepth);
});

test("rejects an invalid limit", () => {
  for (const limit of [0, -1, 1.5, Number.POSITIVE_INFINITY, "2"]) {
    assert.throws(() => createExportDepthGuard(limit), /positive integer/);
  }
});
