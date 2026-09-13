import assert from "node:assert/strict";
import { resolve } from "node:path";
import test from "node:test";
import { runRawModuleInspectionCommand } from "./raw-module-inspection-command.mjs";
import { RawModuleInspectionError } from "./raw-module-inspection-error.mjs";

const modulePath = resolve("application.wasm");
const binaryenPath = resolve("binaryen/index.js");

function fixture(overrides = {}) {
  const calls = [];
  const outputs = [];
  const bytes = new Uint8Array([1, 2]);
  const decoder = {};
  const dependencies = {
    async readModule(path) {
      calls.push(["read", path]);
      return bytes;
    },
    async loadDecoder(path) {
      calls.push(["load", path]);
      return decoder;
    },
    async inspectModule(input, loadedDecoder) {
      calls.push(["inspect", input, loadedDecoder]);
      return [{ module: "host", name: "call", parameters: ["i32", "i64"], results: ["f32"] }];
    },
    writeOutput(output) {
      calls.push(["write", output]);
      outputs.push(output);
    },
    ...overrides,
  };
  return { calls, outputs, bytes, decoder, dependencies };
}

test("command reads exact absolute inputs and writes one versioned success envelope", async () => {
  const { calls, outputs, bytes, decoder, dependencies } = fixture();

  const exitCode = await runRawModuleInspectionCommand(
    [modulePath, binaryenPath], dependencies);

  assert.equal(exitCode, 0);
  assert.deepEqual(calls, [
    ["read", modulePath],
    ["load", binaryenPath],
    ["inspect", bytes, decoder],
    ["write", outputs[0]],
  ]);
  assert.equal(outputs[0], "{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"imports\":[{\"module\":\"host\",\"name\":\"call\",\"parameters\":[\"i32\",\"i64\"],\"results\":[\"f32\"]}]}\n");
});

test("command snapshots inspected imports before asynchronous output", async () => {
  const imports = [{ module: "", name: "", parameters: ["i32"], results: [] }];
  let release;
  let outputStarted;
  const pending = new Promise(resolvePending => { release = resolvePending; });
  const started = new Promise(resolveStarted => { outputStarted = resolveStarted; });
  const { outputs, dependencies } = fixture({
    inspectModule: () => imports,
    async writeOutput(output) {
      outputStarted();
      await pending;
      outputs.push(output);
    },
  });

  const result = runRawModuleInspectionCommand([modulePath, binaryenPath], dependencies);
  await started;
  imports[0].module = "changed";
  imports[0].parameters[0] = "f64";
  release();

  assert.equal(await result, 0);
  assert.match(outputs[0], /\"module\":\"\",\"name\":\"\",\"parameters\":\[\"i32\"\]/);
});

for (const commandArguments of [null, [], [modulePath], [modulePath, binaryenPath, "extra"],
  ["module.wasm", binaryenPath], [modulePath, "binaryen/index.js"], [modulePath, ""],
  [modulePath, 1]]) {
  test(`command rejects invalid argument contract ${JSON.stringify(commandArguments)}`, async () => {
    const { calls, outputs, dependencies } = fixture();

    assert.equal(await runRawModuleInspectionCommand(commandArguments, dependencies), 1);

    assert.deepEqual(calls, [["write", outputs[0]]]);
    assert.equal(outputs[0], "{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":\"invalid-arguments\"}\n");
  });
}

for (const code of ["invalid-bytes", "invalid-core-module", "unsupported-import-kind",
  "unsupported-import-signature", "duplicate-import", "decoder-failure",
  "inconsistent-import-inventory"]) {
  test(`command preserves stable inspection error ${code} without partial imports`, async () => {
    const { outputs, dependencies } = fixture({
      inspectModule() { throw new RawModuleInspectionError(code); },
    });

    assert.equal(await runRawModuleInspectionCommand([modulePath, binaryenPath], dependencies), 1);

    assert.equal(outputs[0], `{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":\"${code}\"}\n`);
    assert.doesNotMatch(outputs[0], /imports/);
  });
}

for (const [stage, overrides] of [
  ["read", { readModule() { throw new Error("read failed"); } }],
  ["load", { loadDecoder() { throw new Error("load failed"); } }],
  ["inspect", { inspectModule() { throw new Error("inspect failed"); } }],
  ["unknown inspection code", { inspectModule() { throw new RawModuleInspectionError("other"); } }],
  ["non-array inspection product", { inspectModule: () => null }],
  ["null import", { inspectModule: () => [null] }],
  ["non-object import", { inspectModule: () => [1] }],
  ["non-string module", { inspectModule: () => [{ module: 1, name: "call", parameters: [], results: [] }] }],
  ["non-string name", { inspectModule: () => [{ module: "host", name: null, parameters: [], results: [] }] }],
  ["non-array parameters", { inspectModule: () => [{ module: "host", name: "call", parameters: null, results: [] }] }],
  ["unsupported parameter", { inspectModule: () => [{ module: "host", name: "call", parameters: ["v128"], results: [] }] }],
  ["non-array results", { inspectModule: () => [{ module: "host", name: "call", parameters: [], results: null }] }],
  ["unsupported result", { inspectModule: () => [{ module: "host", name: "call", parameters: [], results: ["v128"] }] }],
]) {
  test(`command maps ${stage} failure to an opaque command failure`, async () => {
    const { outputs, dependencies } = fixture(overrides);

    assert.equal(await runRawModuleInspectionCommand([modulePath, binaryenPath], dependencies), 1);

    assert.equal(outputs[0], "{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":\"inspection-command-failure\"}\n");
  });
}

test("command stops before decoder load when module reading fails", async () => {
  const { calls, dependencies } = fixture();
  dependencies.readModule = path => {
    calls.push(["read", path]);
    throw new Error("read failed");
  };

  assert.equal(await runRawModuleInspectionCommand(
    [modulePath, binaryenPath], dependencies), 1);

  assert.deepEqual(calls.map(call => call[0]), ["read", "write"]);
});

test("command stops before inspection when decoder loading fails", async () => {
  const { calls, dependencies } = fixture();
  dependencies.loadDecoder = path => {
    calls.push(["load", path]);
    throw new Error("load failed");
  };

  assert.equal(await runRawModuleInspectionCommand(
    [modulePath, binaryenPath], dependencies), 1);

  assert.deepEqual(calls.map(call => call[0]), ["read", "load", "write"]);
});

test("command validates every dependency before performing work", async () => {
  const { dependencies } = fixture();
  await assert.rejects(runRawModuleInspectionCommand(
    [modulePath, binaryenPath], null), TypeError);
  for (const name of ["readModule", "loadDecoder", "inspectModule", "writeOutput"]) {
    await assert.rejects(runRawModuleInspectionCommand(
      [modulePath, binaryenPath], { ...dependencies, [name]: null }), TypeError);
  }
});

test("command propagates its single output failure without attempting a second write", async () => {
  let writes = 0;
  const cause = new Error("write failed");
  const { dependencies } = fixture({
    writeOutput() {
      writes++;
      throw cause;
    },
  });

  await assert.rejects(
    runRawModuleInspectionCommand([modulePath, binaryenPath], dependencies),
    error => error === cause);
  assert.equal(writes, 1);
});
