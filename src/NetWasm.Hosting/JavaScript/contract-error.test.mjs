import assert from "node:assert/strict";
import test from "node:test";

import { createContractError, isContractError } from "./contract-error.mjs";

test("brands only errors created by the contract boundary", () => {
  const error = createContractError();
  assert.equal(error instanceof Error, true);
  assert.equal(isContractError(error), true);
  assert.match(createContractError("specific contract detail").message, /specific/);
  for (const value of [null, 1, {}, new Error("other")]) {
    assert.equal(isContractError(value), false);
  }
});
