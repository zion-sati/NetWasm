// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createContractError, isContractError, unwrapContractError } from "./contract-error.mjs";

test("brands only errors created by the contract boundary", () => {
  const error = createContractError();
  assert.equal(error instanceof Error, true);
  assert.equal(isContractError(error), true);
  assert.match(createContractError("specific contract detail").message, /specific/);
  for (const value of [null, 1, {}, new Error("other")]) {
    assert.equal(isContractError(value), false);
  }
});

test("unwraps contract errors to the value they wrap", () => {
  const cause = new TypeError("not granted");
  const plain = createContractError();
  const other = new Error("outer", { cause });

  assert.equal(createContractError(undefined, cause).cause, cause);
  assert.equal(Object.hasOwn(plain, "cause"), false);
  assert.equal(unwrapContractError(createContractError(undefined, createContractError(undefined, cause))), cause);
  assert.equal(unwrapContractError(plain), plain);
  assert.equal(unwrapContractError(other), other);
  assert.equal(unwrapContractError(undefined), undefined);
});
