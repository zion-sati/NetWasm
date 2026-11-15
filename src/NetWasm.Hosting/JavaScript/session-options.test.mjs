// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import {
  consoleLine,
  createLineOutput,
  mergeEnvironment,
  readEnvironmentOverride,
} from "./session-options.mjs";

test("reads plain string environment overrides", () => {
  assert.equal(readEnvironmentOverride(undefined, "env"), undefined);
  const value = readEnvironmentOverride(Object.assign(Object.create(null), { MODE: "a" }), "env");
  assert.deepEqual({ ...value }, { MODE: "a" });
  assert.equal(Object.isFrozen(value), true);
  for (const invalid of [null, [], "MODE=a", new Map(), { [Symbol("x")]: "a" },
    Object.defineProperty({}, "MODE", { enumerable: true, get: () => "a" })]) {
    assert.throws(() => readEnvironmentOverride(invalid, "env"), /env must be a plain object/);
  }
  assert.throws(() => readEnvironmentOverride({ MODE: null }, "env"), /env values must be strings/);
});

test("merges environment overrides and keeps the configuration otherwise", () => {
  const configuration = Object.freeze({
    environment: Object.freeze([Object.freeze({ name: "A", value: "1" })]),
    grants: Object.freeze({ clocks: Object.freeze([]), environment: Object.freeze(["A"]) }),
  });
  assert.equal(mergeEnvironment(configuration, undefined), configuration);
  const merged = mergeEnvironment(configuration, { A: "2", B: "3" });
  assert.deepEqual(merged.environment, [{ name: "A", value: "2" }, { name: "B", value: "3" }]);
  assert.deepEqual(merged.grants, { clocks: [], environment: ["A", "B"] });
});

test("delivers complete lines, joins split characters and flushes the remainder", () => {
  const lines = [];
  const output = createLineOutput(line => lines.push(line));
  const bytes = new TextEncoder().encode("a\r\né\n");
  output.sink.write(bytes.subarray(0, 4));
  output.sink.write(bytes.subarray(4));
  output.flush();
  output.sink.write(new TextEncoder().encode("tail"));
  output.flush();
  output.flush();
  assert.deepEqual(lines, ["a", "é", "tail"]);
  assert.throws(() => createLineOutput(null), /line action/);
});

test("writes console lines at the requested level", () => {
  const original = console.warn;
  const logged = [];
  console.warn = line => logged.push(line);
  try {
    consoleLine("warn")("line");
  } finally {
    console.warn = original;
  }
  assert.deepEqual(logged, ["line"]);
});
