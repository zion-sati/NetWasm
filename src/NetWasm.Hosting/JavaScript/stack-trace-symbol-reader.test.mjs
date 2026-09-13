import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";

test("parseStackTraceSymbols accepts absent and valid immutable symbol data", () => {
  assert.deepEqual(parseStackTraceSymbols(null), []);
  assert.deepEqual(parseStackTraceSymbols(undefined), []);

  const source = {
    schemaVersion: 1,
    methods: [
      { id: 1, name: "Example.Program.Main()" },
      { id: 0xffffffff, name: "Example.Worker.Run😀()" },
    ],
  };
  const parsedObject = parseStackTraceSymbols(source);
  const parsedJson = parseStackTraceSymbols(JSON.stringify(source));

  assert.deepEqual(parsedObject, source.methods);
  assert.deepEqual(parsedJson, source.methods);
  assert.notStrictEqual(parsedObject[0], source.methods[0]);
  assert.equal(Object.isFrozen(parsedObject[0]), true);
});

test("parseStackTraceSymbols rejects malformed documents and envelopes", () => {
  assert.throws(
    () => parseStackTraceSymbols("{"),
    error => error instanceof NetWasmHostError && /invalid.*sidecar/i.test(error.message));

  for (const invalid of [
    "null",
    42,
    {},
    { schemaVersion: 2, methods: [] },
    { schemaVersion: 1 },
    { schemaVersion: 1, methods: {} },
  ]) {
    assert.throws(
      () => parseStackTraceSymbols(invalid),
      error => error instanceof NetWasmHostError && /schema/i.test(error.message));
  }
});

test("parseStackTraceSymbols rejects invalid and duplicate methods", () => {
  for (const method of [
    null,
    42,
    {},
    { id: 1.5, name: "A" },
    { id: 0, name: "A" },
    { id: 0x100000000, name: "A" },
    { id: 1, name: 42 },
    { id: 1, name: "" },
  ]) {
    assert.throws(
      () => parseStackTraceSymbols({ schemaVersion: 1, methods: [method] }),
      error => error instanceof NetWasmHostError && /index 0/i.test(error.message));
  }

  assert.throws(() => parseStackTraceSymbols({
    schemaVersion: 1,
    methods: [{ id: 1, name: "A" }, { id: 1, name: "B" }],
  }), error => error instanceof NetWasmHostError && /duplicate.*1/i.test(error.message));
});
