import assert from "node:assert/strict";
import test from "node:test";
import { readBinaryenFunctionImports } from "./binaryen-function-import-reader.mjs";
import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

function fixture(functions = []) {
  const observations = { disposed: 0, reads: 0, expanded: [] };
  const module = {
    getNumFunctions: () => functions.length,
    getFunctionByIndex: index => index,
    dispose() { observations.disposed++; },
  };
  const decoder = {
    i32: 1, i64: 2, f32: 3, f64: 4, Features: { All: 255 },
    readBinary(bytes, features) {
      observations.reads++;
      observations.bytes = bytes;
      assert.equal(features, 255);
      return module;
    },
    getFunctionInfo: index => functions[index],
    expandType(type) { observations.expanded.push(type); return type; },
  };
  return { decoder, module, observations };
}

test("decoder adapter projects numeric tuples, empty names and void while ignoring defined functions", () => {
  const functions = [
    { body: 17, module: "ignored", base: "defined", params: [99], results: [99] },
    { body: 0, module: "", base: "", params: [1, 2, 3, 4, 1], results: [4, 3, 2, 1] },
    { body: 0, module: "host", base: "empty", params: [], results: [] },
  ];
  const { decoder, observations } = fixture(functions);
  const bytes = new Uint8Array([1, 2]);
  const result = readBinaryenFunctionImports(bytes, decoder);
  assert.deepEqual(result, [
    { module: "", name: "", parameters: ["i32", "i64", "f32", "f64", "i32"],
      results: ["f64", "f32", "i64", "i32"] },
    { module: "host", name: "empty", parameters: [], results: [] },
  ]);
  assert.equal(observations.disposed, 1);
  assert.equal(observations.reads, 1);
  assert.notEqual(observations.bytes, bytes);
  assert.deepEqual(observations.bytes, bytes);
  observations.bytes.fill(9);
  assert.deepEqual(bytes, new Uint8Array([1, 2]));
  assert.deepEqual(observations.expanded, functions.slice(1).flatMap(info => [info.params, info.results]));
  assert.ok(Object.isFrozen(result));
  assert.ok(result.every(entry => Object.isFrozen(entry)
    && Object.isFrozen(entry.parameters) && Object.isFrozen(entry.results)));
});

test("decoder adapter accepts a module with no functions and disposes it", () => {
  const { decoder, observations } = fixture();
  assert.deepEqual(readBinaryenFunctionImports(new Uint8Array(), decoder), []);
  assert.equal(observations.disposed, 1);
});

test("decoder adapter rejects invalid bytes before reading", () => {
  const { decoder, observations } = fixture();
  assert.throws(() => readBinaryenFunctionImports(null, decoder), { code: "invalid-bytes" });
  assert.equal(observations.reads, 0);
});

test("decoder adapter rejects missing external API capabilities", () => {
  const { decoder, observations } = fixture();
  for (const value of [null, {}, { ...decoder, readBinary: 1 },
    { ...decoder, getFunctionInfo: null }, { ...decoder, expandType: null },
    { ...decoder, Features: null }, { ...decoder, Features: {} }]) {
    assert.throws(() => readBinaryenFunctionImports(new Uint8Array(), value), TypeError);
  }
  assert.equal(observations.reads, 0);
});

for (const position of ["params", "results"]) {
  test(`decoder adapter rejects unsupported imported ${position} and disposes after projection failure`, () => {
    const { decoder, observations } = fixture([
      { body: 0, module: "host", base: "member", params: [], results: [], [position]: [99] },
    ]);
    assert.throws(() => readBinaryenFunctionImports(new Uint8Array(), decoder), error => {
      assert.ok(error instanceof RawModuleInspectionError);
      assert.equal(error.code, "unsupported-import-signature");
      return true;
    });
    assert.equal(observations.disposed, 1);
  });
}

test("decoder adapter preserves a read failure without disposing an unacquired module", () => {
  const { decoder, observations } = fixture();
  const cause = new Error("read rejected");
  decoder.readBinary = () => { throw cause; };
  assert.throws(() => readBinaryenFunctionImports(new Uint8Array(), decoder), error =>
    error.code === "decoder-failure" && error.cause === cause);
  assert.equal(observations.disposed, 0);
});

test("decoder adapter disposes after an external inspection failure and preserves its cause", () => {
  const { decoder, observations } = fixture([{}]);
  const cause = new Error("inspection rejected");
  decoder.getFunctionInfo = () => { throw cause; };
  assert.throws(() => readBinaryenFunctionImports(new Uint8Array(), decoder), error =>
    error.code === "decoder-failure" && error.cause === cause);
  assert.equal(observations.disposed, 1);
});

test("decoder adapter does not return output after a disposal failure", () => {
  const { decoder, module } = fixture();
  const cause = new Error("disposal rejected");
  module.dispose = () => { throw cause; };
  assert.throws(() => readBinaryenFunctionImports(new Uint8Array(), decoder), error =>
    error.code === "decoder-failure" && error.cause === cause);
});
