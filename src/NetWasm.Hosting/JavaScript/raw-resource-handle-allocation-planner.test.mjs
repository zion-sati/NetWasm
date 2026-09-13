import assert from "node:assert/strict";
import test from "node:test";
import { planRawResourceHandleAllocation } from "./raw-resource-handle-allocation-planner.mjs";

test("plans the complete fresh-handle range without an artificial capacity", () => {
  const first = planRawResourceHandleAllocation(1);
  assert.deepEqual(first, { handle: 1, nextHandle: 2 });
  assert.equal(Object.isFrozen(first), true);

  assert.deepEqual(
    planRawResourceHandleAllocation(0xffff_fffe),
    { handle: 0xffff_fffe, nextHandle: 0xffff_ffff });
  assert.deepEqual(
    planRawResourceHandleAllocation(0xffff_ffff),
    { handle: 0xffff_ffff, nextHandle: null });
});

test("rejects an exhausted or invalid fresh-handle state", () => {
  assert.throws(() => planRawResourceHandleAllocation(null), RangeError);
  for (const value of [undefined, -1, 0, 1.5, 0x1_0000_0000]) {
    assert.throws(() => planRawResourceHandleAllocation(value), TypeError);
  }
});
