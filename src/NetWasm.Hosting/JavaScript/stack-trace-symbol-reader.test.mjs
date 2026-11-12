import assert from "node:assert/strict";
import test from "node:test";

import { NetWasmHostError } from "./managed-errors.mjs";
import { parseStackTraceSymbols } from "./stack-trace-symbol-reader.mjs";
import { symbolSidecar } from "./symbol-sidecar-reader.test.mjs";

const methodSymbols = (methods, options) =>
  symbolSidecar(options?.kind ?? 2, methods.map(({ id, name }) => ({ id, strings: [name] })), options);

const methods = [
  { id: 1, name: "Example.Program.Main()" },
  { id: 0x7fffffff, name: "Example.Worker.Run😀()" },
];

test("parseStackTraceSymbols accepts absent symbols, symbol arrays and method-symbols bytes", () => {
  assert.deepEqual(parseStackTraceSymbols(null), []);
  assert.deepEqual(parseStackTraceSymbols(undefined), []);

  const bytes = methodSymbols(methods);
  const parsedBytes = parseStackTraceSymbols(bytes);
  const parsedBuffer = parseStackTraceSymbols(bytes.buffer);
  const parsedMethods = parseStackTraceSymbols(methods);

  assert.deepEqual(parsedBytes, methods);
  assert.deepEqual(parsedBuffer, methods);
  assert.deepEqual(parsedMethods, methods);
  assert.notStrictEqual(parsedMethods[0], methods[0]);
  assert.equal(Object.isFrozen(parsedBytes[0]), true);
});

test("parseStackTraceSymbols rejects values that are not a method-symbols sidecar", () => {
  for (const invalid of ["{", 42, {}]) {
    assert.throws(
      () => parseStackTraceSymbols(invalid),
      error => error instanceof NetWasmHostError && /must be bytes/i.test(error.message));
  }
  for (const invalid of [
    methodSymbols(methods, { magic: 0 }),
    methodSymbols(methods, { kind: 1 }),
    methodSymbols(methods).slice(0, 40),
    methodSymbols([{ id: 2, name: "B" }, { id: 1, name: "A" }]),
  ]) {
    assert.throws(
      () => parseStackTraceSymbols(invalid),
      error => error instanceof NetWasmHostError && /invalid NetWasm method-symbols sidecar/i.test(error.message));
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
      () => parseStackTraceSymbols([method]),
      error => error instanceof NetWasmHostError && /index 0/i.test(error.message));
  }
  assert.throws(
    () => parseStackTraceSymbols(methodSymbols([{ id: 0, name: "A" }])),
    error => error instanceof NetWasmHostError && /index 0/i.test(error.message));

  assert.throws(() => parseStackTraceSymbols([{ id: 1, name: "A" }, { id: 1, name: "B" }]),
    error => error instanceof NetWasmHostError && /duplicate.*1/i.test(error.message));
});
