import assert from "node:assert/strict";
import test from "node:test";

import { createNetWasmExecutionFacade } from "./netwasm-execution-facade.mjs";

test("delegates the exact public request and result", async () => {
  const request = Object.freeze({ request: true });
  const result = Object.freeze({ result: true });
  let received;
  const executeNetWasm = createNetWasmExecutionFacade(value => {
    received = value;
    return result;
  });
  assert.equal(Object.isFrozen(executeNetWasm), true);
  assert.equal(executeNetWasm.name, "executeNetWasm");
  assert.equal(await executeNetWasm(request), result);
  assert.equal(received, request);
});

test("rejects a missing deployment execution action", () => {
  for (const value of [undefined, null, {}, "execute"]) {
    assert.throws(() => createNetWasmExecutionFacade(value), TypeError);
  }
});
