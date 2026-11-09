import assert from "node:assert/strict";
import test from "node:test";
import { createDiagnosticCommandExport } from "./diagnostic-command-export.mjs";
import { executeCommand } from "./command-executor.mjs";
import { managedExceptionBrand } from "./managed-errors.mjs";

test("preserves ordinary command exits without producing terminal events", async () => {
  for (const val of [0, 1]) {
    const run = createDiagnosticCommandExport(() => ({ tag: "exited", val }),
      () => assert.fail("ordinary exit must not report an exception"));
    const result = await executeCommand({ run });
    assert.equal(result.completionKind, "normal");
    assert.equal(result.exitCode, val);
  }
  const run = createDiagnosticCommandExport(() => ({ tag: "exited", val: undefined }), () => {});
  assert.equal((await executeCommand({ run })).completionKind, "contractFailure");
});

test("reports exact typed payloads and brands the managed failure", async () => {
  for (const [message, stackTrace] of [
    [undefined, undefined], [null, null],
    [new Uint16Array(), new Uint16Array()],
    [Uint16Array.of(0x41, 0xd800, 0x42), Uint16Array.of(0xdc00)],
  ]) {
    const events = [];
    const run = createDiagnosticCommandExport(
      () => ({ tag: "failed", val: { typeId: 7, message, stackTrace } }),
      (...args) => events.push(args));
    await assert.rejects(run, error => error[managedExceptionBrand] === true
      && error.managedType === 7 && error.exportName === "run");
    assert.deepEqual(events, [[7, message ?? null, stackTrace ?? null]]);
    assert.equal((await executeCommand({ run })).completionKind, "managedFailure");
  }
});

test("propagates invocation and reporting failures without fabricating managed failures", async () => {
  for (const failure of [new WebAssembly.RuntimeError("trap"), new Error("provider")]) {
    let reports = 0;
    const run = createDiagnosticCommandExport(() => { throw failure; }, () => reports++);
    await assert.rejects(run, error => error === failure);
    assert.equal(reports, 0);
    assert.equal((await executeCommand({ run })).completionKind, "hostFailure");
  }
  const failure = new TypeError("reporter rejected payload");
  const run = createDiagnosticCommandExport(
    () => ({ tag: "failed", val: { typeId: 7, message: undefined, stackTrace: undefined } }),
    () => { throw failure; });
  await assert.rejects(run, error => error === failure);
  assert.equal((await executeCommand({ run })).completionKind, "hostFailure");
});

test("rejects incomplete actor dependencies", async () => {
  assert.throws(() => createDiagnosticCommandExport(null, () => {}), TypeError);
  assert.throws(() => createDiagnosticCommandExport(() => {}, null), TypeError);
});

test("rejects malformed completions and events before reporting", async () => {
  for (const completion of [null, undefined, 1, [], {}, { tag: "unknown" },
    ...[null, undefined, 1, [], {}, { typeId: 7 }, { typeId: 7, message: null }]
      .map(val => ({ tag: "failed", val }))]) {
    const run = createDiagnosticCommandExport(() => completion,
      () => assert.fail("malformed completion must not report"));
    await assert.rejects(run, TypeError);
    assert.equal((await executeCommand({ run })).completionKind, "hostFailure");
  }
});
