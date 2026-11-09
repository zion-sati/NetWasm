import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { commandComponentContract } from "./canonical-component-binder.mjs";
import { executeComponent } from "./component-executor.mjs";

const loadCoreModule = async () => ({});
const instantiateCore = async () => ({});
const release = (code, action) => ({
  code,
  message: `The execution host could not complete ${code}.`,
  release: action,
});
const adapter = (contractKey, instantiate) => ({ contractKey, instantiate });

test("executes one exact command adapter and closes its caller scope", async () => {
  const calls = [];
  const imports = { "wasi:cli/environment": Object.freeze({}) };
  const result = await executeComponent({
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, function (request) {
      assert.equal(this.contractKey, commandComponentContract);
      assert.equal(Object.isFrozen(request), true);
      assert.equal(Object.isFrozen(request.imports), true);
      assert.equal(request.loadCoreModule, loadCoreModule);
      assert.equal(request.instantiateCore, instantiateCore);
      assert.equal(request.imports["wasi:cli/environment"], imports["wasi:cli/environment"]);
      calls.push("instantiate");
      return { command: { run() { calls.push("run"); return 23; } } };
    }),
    imports,
    loadCoreModule,
    instantiateCore,
    releaseActions: [release("host.output", () => calls.push("release"))],
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 23);
  assert.deepEqual(calls, ["instantiate", "run", "release"]);
});

test("reports and classifies a terminal managed command failure", async () => {
  const encoder = new TextEncoder();
  const wasmBytes = Uint8Array.of(0, 97, 115, 109);
  const mapBytes = encoder.encode(JSON.stringify({
    schemaVersion: 2,
    buildId: "build",
    entries: [{
      typeId: 7,
      displayName: "System.FormatException",
      canonicalIdentity: "System.FormatException, System.Private.CoreLib",
      assemblyIdentity: "System.Private.CoreLib",
    }],
  }));
  const digest = bytes => createHash("sha256").update(bytes).digest("hex");
  const immediate = [];
  const enriched = [];
  const result = await executeComponent({
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, ({ imports }) => ({
      command: {
        run() {
          imports["netwasm:diagnostics/terminal"].report(
            7,
            Uint16Array.from([..."invalid format"].map(value => value.charCodeAt(0))),
            Uint16Array.from([..."at method#7"]
              .map(value => value.charCodeAt(0))));
          throw new WebAssembly.RuntimeError("terminal trap");
        },
      },
    })),
    imports: {},
    loadCoreModule,
    diagnosticArtifacts: {
      manifest: {
        schemaVersion: 1,
        buildId: "build",
        wasmSha256: digest(wasmBytes),
        exceptionTypeMapSha256: digest(mapBytes),
      },
      wasmBytes,
      mapBytes,
      mapMediaType: "application/vnd.netwasm.exception-types+json;version=2",
    },
    managedExceptionReporting: {
      reportImmediate: event => immediate.push(event),
      reportEnriched: event => enriched.push(event),
    },
    stackTraceSymbols: [{
      id: 7,
      name: "EntryPoint.Run in Program.cs:line 12",
    }],
  });

  assert.equal(result.completionKind, "managedFailure");
  assert.equal(result.primaryFailure.code, "managed.failure");
  assert.equal(immediate[0].message, "invalid format");
  assert.equal(immediate[0].stackTrace, "at EntryPoint.Run in Program.cs:line 12");
  assert.equal(enriched[0].typeName, "System.FormatException");
  assert.equal(enriched[0].stackTrace, "at EntryPoint.Run in Program.cs:line 12");
});

test("rejects adapter mismatch and pre-cancellation before instantiation", async () => {
  for (const expected of [
    ["mismatch", "contractFailure", "contract.component-adapter"],
    ["cancel", "callerCancellation", "caller.cancelled"],
  ]) {
    const calls = [];
    const controller = new AbortController();
    if (expected[0] === "cancel") controller.abort();
    const result = await executeComponent({
      contractKey: commandComponentContract,
      adapter: adapter(
        expected[0] === "mismatch" ? "netwasm:runtime/process@1.0.0" : commandComponentContract,
        () => calls.push("instantiate")),
      imports: {},
      loadCoreModule,
      signal: controller.signal,
      releaseActions: [release("host.output", () => calls.push("release"))],
    });
    assert.equal(result.completionKind, expected[1]);
    assert.equal(result.primaryFailure.code, expected[2]);
    assert.deepEqual(calls, ["release"]);
  }
});

test("maps command instantiation, binding and cleanup failures with their causes", async () => {
  const cases = [
    [() => { throw new Error("private instantiate detail"); },
      "hostFailure", "host.component-instantiate", "Error: private instantiate detail"],
    [() => ({}), "contractFailure", "contract.component-exports",
      "TypeError: canonical command component is incomplete"],
  ];
  for (const [instantiate, kind, code, detail] of cases) {
    const result = await executeComponent({
      contractKey: commandComponentContract,
      adapter: adapter(commandComponentContract, instantiate),
      imports: {},
      loadCoreModule,
      releaseActions: [release("host.output", () => {
        throw new Error("private release detail");
      })],
    });
    assert.equal(result.completionKind, kind);
    assert.equal(result.primaryFailure.code, code);
    assert.deepEqual(result.cleanupFailures.map(failure => failure.code), ["host.output"]);
    assert.equal(result.primaryFailure.detail, detail);
    assert.deepEqual(result.cleanupFailures.map(failure => failure.detail),
      ["Error: private release detail"]);
  }
});

test("validates every component dependency before adapter invocation", async () => {
  let instantiated = 0;
  const valid = {
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, () => { instantiated++; }),
    imports: {},
    loadCoreModule,
  };
  for (const request of [null, 1, []]) {
    await assert.rejects(() => executeComponent(request), TypeError);
  }
  for (const contractKey of [undefined, "", "wasi-command@0.2.11"]) {
    await assert.rejects(() => executeComponent({ ...valid, contractKey }), TypeError);
  }
  for (const value of [null, 1, [], {}, { contractKey: "x", instantiate() {}, extra: true },
    { contractKey: null, instantiate() {} }, { contractKey: "x", instantiate: null }]) {
    await assert.rejects(() => executeComponent({ ...valid, adapter: value }), TypeError);
  }
  for (const imports of [null, 1, [], Object.create({ inherited: {} }),
    { "": {} }, { invalid: null }, { invalid: () => {} }]) {
    await assert.rejects(() => executeComponent({ ...valid, imports }), TypeError);
  }
  const symbolImports = { valid: {} };
  symbolImports[Symbol("invalid")] = {};
  await assert.rejects(() => executeComponent({ ...valid, imports: symbolImports }), TypeError);
  const hiddenImports = {};
  Object.defineProperty(hiddenImports, "hidden", { value: {}, enumerable: false });
  await assert.rejects(() => executeComponent({ ...valid, imports: hiddenImports }), TypeError);
  const accessorImports = {};
  Object.defineProperty(accessorImports, "getter", { get: () => ({}), enumerable: true });
  await assert.rejects(() => executeComponent({ ...valid, imports: accessorImports }), TypeError);
  for (const reserved of ["netwasm:diagnostics/terminal",
    "netwasm:diagnostics/terminal@1.0.0"]) {
    await assert.rejects(() => executeComponent({
      ...valid,
      imports: { [reserved]: {} },
    }), /reserved/);
  }
  await assert.rejects(() => executeComponent({ ...valid, loadCoreModule: null }), TypeError);
  await assert.rejects(() => executeComponent({ ...valid, instantiateCore: 1 }), TypeError);
  await assert.rejects(() => executeComponent({ ...valid, signal: {} }), TypeError);
  await assert.rejects(() => executeComponent({ ...valid, releaseActions: null }), TypeError);
  assert.equal(instantiated, 0);
});

test("prefers typed command completion and preserves reporting without custom imports", async () => {
  const reports = [];
  let standardInvocations = 0;
  const result = await executeComponent({
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, () => ({
      command: { run() { standardInvocations++; return 0; } },
      diagnosticCommand: { run() {
        return { tag: "failed", val: {
          typeId: 7,
          message: Uint16Array.of(0x41, 0xd800, 0x42),
          stackTrace: undefined,
        } };
      } },
    })),
    imports: {},
    loadCoreModule,
    managedExceptionReporting: {
      reportImmediate: event => reports.push(event),
      reportEnriched() {},
    },
  });
  assert.equal(standardInvocations, 0);
  assert.equal(result.completionKind, "managedFailure");
  assert.equal(reports.length, 1);
  assert.equal(reports[0].typeId, 7);
  assert.equal(reports[0].message, "A\ud800B");
  assert.equal(reports[0].stackTrace, null);
});

test("awaits an asynchronous run export before closing the caller scope", async () => {
  const calls = [];
  const result = await executeComponent({
    contractKey: commandComponentContract,
    adapter: adapter(commandComponentContract, () => ({
      command: {
        async run() {
          await new Promise(resolve => setTimeout(resolve, 1));
          calls.push("run");
          return 4;
        },
      },
    })),
    imports: {},
    loadCoreModule,
    releaseActions: [release("host.output", () => calls.push("release"))],
  });
  assert.equal(result.exitCode, 4);
  assert.deepEqual(calls, ["run", "release"]);
});
