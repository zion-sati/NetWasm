import assert from "node:assert/strict";
import test from "node:test";
import {
  exceptionClauseSize,
  exceptionClauseValueOffset,
  readExceptionClause,
} from "./exception-metadata.mjs";

test("exception clauses retain their fixed u32 ABI under wasm32 and wasm64", () => {
  var bytes = new ArrayBuffer(64);
  var view = new DataView(bytes);
  var metadata = 8;
  view.setUint32(metadata, 0, true);
  view.setUint32(metadata + exceptionClauseValueOffset, 17, true);
  view.setUint32(metadata + exceptionClauseSize, 1, true);
  view.setUint32(
    metadata + exceptionClauseSize + exceptionClauseValueOffset,
    29,
    true);

  var wasm32 = readExceptionClause(
    view,
    metadata,
    1,
    (address, offset) => address + offset,
    Number);
  var wasm64 = readExceptionClause(
    view,
    BigInt(metadata),
    1,
    (address, offset) => address + BigInt(offset),
    Number);

  assert.deepEqual(wasm32, { kind: 1, value: 29 });
  assert.deepEqual(wasm64, wasm32);
});
