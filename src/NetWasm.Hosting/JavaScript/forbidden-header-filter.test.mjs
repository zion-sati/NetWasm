// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { isForbiddenFieldName, withoutForbiddenFields } from "./forbidden-header-filter.mjs";

test("connection-level names are forbidden whatever their case", () => {
  for (const name of ["Host", "connection", "Transfer-Encoding", "upgrade", "keep-alive", "proxy-authorization"]) {
    assert.equal(isForbiddenFieldName(name), true, name);
  }
  assert.equal(isForbiddenFieldName("content-type"), false);
});

test("a platform request's headers keep all but the forbidden ones", () => {
  assert.deepEqual(
    withoutForbiddenFields([["host", "a.test"], ["accept", "*/*"], ["Connection", "close"], ["x-id", "7"]]),
    [["accept", "*/*"], ["x-id", "7"]]);
});
