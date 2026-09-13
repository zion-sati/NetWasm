import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { parseStrictJson } from "./strict-json-reader.mjs";

test("parseStrictJson preserves every JSON value kind", () => {
  assert.equal(parseStrictJson("null"), null);
  assert.equal(parseStrictJson("true"), true);
  assert.equal(parseStrictJson("42"), 42);
  assert.equal(parseStrictJson("\"text\""), "text");
  assert.deepEqual(parseStrictJson("[]"), []);
  assert.deepEqual(parseStrictJson("{}"), {});
  assert.deepEqual(parseStrictJson(` {
    "text": "escaped \\\" value",
    "array": [1, { "nested": false }, []]
  } `), {
    text: "escaped \" value",
    array: [1, { nested: false }, []],
  });
});

test("parseStrictJson rejects non-text and malformed input with a stable error", () => {
  assert.throws(
    () => parseStrictJson(new Uint8Array()),
    error => error instanceof NetWasmHostError && /must be text/i.test(error.message));
  assert.throws(
    () => parseStrictJson("{"),
    error => error instanceof NetWasmHostError
      && /invalid JSON/i.test(error.message)
      && error.cause instanceof SyntaxError);
});

test("parseStrictJson rejects duplicate decoded property names at every depth", () => {
  for (const text of [
    '{"name":1,"name":2}',
    '{"name":1,"\\u006eame":2}',
    '{"outer":{"name":1,"name":2}}',
    '[{"name":1,"name":2}]',
  ]) {
    assert.throws(
      () => parseStrictJson(text),
      error => error instanceof NetWasmHostError && /duplicate property 'name'/i.test(error.message));
  }
});
