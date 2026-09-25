import assert from "node:assert/strict";
import test from "node:test";
import { createRefreshableDataView } from "./refreshable-data-view.mjs";

test("methods and properties follow WebAssembly memory growth", () => {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const view = createRefreshableDataView(memory);

  view.setInt32(0, 0x12345678, true);
  assert.equal(view.getInt32(0, true), 0x12345678);
  assert.equal(view.byteLength, 65_536);
  assert.equal(view.buffer, memory.buffer);

  memory.grow(1);

  assert.equal(view.byteLength, 131_072);
  assert.equal(view.buffer, memory.buffer);
  assert.equal(view.getInt32(0, true), 0x12345678);
  view.setInt32(65_536, 0x23456789, true);
  assert.equal(view.getInt32(65_536, true), 0x23456789);
});
