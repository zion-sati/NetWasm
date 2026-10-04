import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";
import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { readManagedExceptionDetails } from "./managed-exception-details.mjs";
import { createManagedExceptionCapture } from "./managed-exception-capture.mjs";
import { createManagedExceptionPayloadConsumer } from "./managed-exception-payload-consumer.mjs";
import { createManagedExceptionTypeResolver } from "./managed-exception-type-resolver.mjs";

const details = (overrides = {}) => ({ typeId: 7, typeName: null, message: "message", stackTrace: null, ...overrides });

test("snapshots clone-safe managed details while preserving null, empty and Unicode", () => {
  for (const message of [null, "", "message 🌏\ud800"])
    for (const stackTrace of [null, "", "at Program.Run in Program.cs:line 12"])
      for (const typeName of [null, "System.FormatException"]) {
        const source = details({ message, stackTrace, typeName });
        const result = readManagedExceptionDetails(source);
        source.message = "changed";
        assert.deepEqual(result, details({ message, stackTrace, typeName }));
        assert.equal(Object.isFrozen(result), true);
        assert.deepEqual(structuredClone(result), result);
      }
  const source = Object.assign(Object.create(null), details());
  assert.deepEqual(readManagedExceptionDetails(source), details());
  assert.equal(readManagedExceptionDetails(details({ typeId: 0x7fffffff })).typeId, 0x7fffffff);
});

test("rejects malformed managed details without invoking accessors", () => {
  const getter = Object.defineProperty(details(), "message", { enumerable: true, get() { assert.fail("getter"); } });
  for (const value of [
    null, undefined, 1, [], { ...details(), [Symbol()]: true }, {},
    { ...details(), extra: true }, { ...details(), wrong: true, message: undefined },
    { ...details(), typeId: 0 }, { ...details(), typeId: 1.5 }, { ...details(), typeId: 0x80000000 },
    { ...details(), typeName: "" }, { ...details(), typeName: 1 },
    { ...details(), message: 1 }, { ...details(), stackTrace: 1 },
    getter, Object.defineProperty(details(), "message", { enumerable: false, value: "hidden" }),
    Object.assign(Object.create({}), details()),
  ]) assert.throws(() => readManagedExceptionDetails(value), TypeError);
  const wrongKey = details(); delete wrongKey.message; wrongKey.other = "message";
  assert.throws(() => readManagedExceptionDetails(wrongKey), TypeError);
});

test("copies rooted managed data before acquiring its call-owned host handle", () => {
  for (const stringDataOffset of [8, 12]) {
    const memory = new WebAssembly.Memory({ initial: 1 });
    const write = (reference, text) => new Uint16Array(memory.buffer, reference + stringDataOffset, text.length)
      .set(Array.from({ length: text.length }, (_, index) => text.charCodeAt(index)));
    const message = "payload 🌏\ud800";
    const trace = "at method#7\nat method#8\nother";
    write(32, message); write(128, trace);
    const handles = createInteropHandleTable();
    const capture = createManagedExceptionCapture({
      getMemory: () => memory, stringDataOffset,
      stackTraceSymbols: [{ id: 7, name: "Program.Run in Program.cs:line 12" }],
      acquireHandle(value) {
        new Uint8Array(memory.buffer).fill(0);
        memory.grow(1);
        return handles.acquire(value);
      },
    });
    const handle = capture(7, 32, message.length, 128, trace.length);
    assert.equal(Object.isFrozen(capture), true);
    assert.deepEqual(handles.get(handle), details({ message,
      stackTrace: "at Program.Run in Program.cs:line 12\nat method#8\nother" }));
    assert.deepEqual(handles.get(capture(7, 32, 0, 128, 0)), details({ message: "", stackTrace: "" }));
    assert.deepEqual(handles.get(capture(7, 0, 0, 0, 0)), details({ message: null }));
    assert.throws(() => capture(0, 0, 0, 0, 0), TypeError);
    assert.equal(handles.count, 3);
  }
});

test("capture validates composition and preserves acquisition failures", () => {
  const cause = new Error("capture failed");
  const options = { getMemory: () => new WebAssembly.Memory({ initial: 1 }), stringDataOffset: 8,
    acquireHandle() { throw cause; }, stackTraceSymbols: [] };
  for (const override of [{ getMemory: null }, { acquireHandle: null },
    { stringDataOffset: -1 }, { stringDataOffset: 0.5 }, { stackTraceSymbols: null }]) {
    assert.throws(() => createManagedExceptionCapture({ ...options, ...override }), TypeError);
  }
  assert.throws(() => createManagedExceptionCapture(options)(7, 0, 0, 0, 0), error => error === cause);
});

test("consumption releases host ownership before overlapping symbol resolution", async () => {
  const handles = createInteropHandleTable();
  const first = handles.acquire(details({ message: "first" }));
  const second = handles.acquire(details({ message: "second" }));
  const lookups = [];
  const consume = createManagedExceptionPayloadConsumer({ getHandle: handles.get, releaseHandle: handles.release,
    resolveType: () => new Promise(resolve => lookups.push(resolve)) });
  assert.equal(await consume(0), null);
  const a = consume(first); const b = consume(second);
  assert.equal(handles.count, 0);
  assert.equal(lookups.length, 2);
  lookups[1]("SecondException"); lookups[0]("FirstException");
  assert.deepEqual(await a, details({ message: "first", typeName: "FirstException" }));
  assert.deepEqual(await b, details({ message: "second", typeName: "SecondException" }));
  await assert.rejects(consume(first), /stale/);
  const signed = createManagedExceptionPayloadConsumer({
    getHandle(handle) { assert.equal(handle, 0xffffffff); return details(); },
    releaseHandle(handle) { assert.equal(handle, 0xffffffff); }, resolveType: () => null,
  });
  assert.deepEqual(await signed(-1), details());
  await assert.rejects(signed(1.5), /i32/);
});

test("consumption releases malformed payloads and propagates owned release or lookup failures", async () => {
  const handles = createInteropHandleTable();
  const consume = createManagedExceptionPayloadConsumer({ getHandle: handles.get,
    releaseHandle: handles.release, resolveType: () => null });
  await assert.rejects(consume(handles.acquire({})), TypeError);
  assert.equal(handles.count, 0);
  const failure = new Error("failed");
  for (const operation of ["getHandle", "releaseHandle", "resolveType"]) {
    const calls = [];
    const options = {
      getHandle() { calls.push("getHandle"); return details(); },
      releaseHandle() { calls.push("releaseHandle"); },
      resolveType() { calls.push("resolveType"); return null; },
    };
    options[operation] = () => { calls.push(operation); throw failure; };
    await assert.rejects(createManagedExceptionPayloadConsumer(options)(1), error => error === failure);
    assert.equal(calls.includes("releaseHandle"), true);
    assert.equal(calls.includes("resolveType"), operation === "resolveType");
  }
  const valid = { getHandle() {}, releaseHandle() {}, resolveType() {} };
  for (const key of Object.keys(valid)) {
    assert.throws(() => createManagedExceptionPayloadConsumer({ ...valid, [key]: null }), TypeError);
  }
});

test("type resolution verifies artifacts once and preserves numeric fallback", async () => {
  const digest = bytes => createHash("sha256").update(bytes).digest("hex");
  const mapBytes = new TextEncoder().encode(JSON.stringify({ schemaVersion: 2, buildId: "build",
    entries: [{ typeId: 7, displayName: "System.FormatException", canonicalIdentity: "format", assemblyIdentity: "core" }] }));
  const wasmBytes = new Uint8Array([1]);
  let loads = 0;
  const resolve = createManagedExceptionTypeResolver({ crypto: webcrypto, loadArtifacts: async () => {
    loads++;
    return { manifest: { schemaVersion: 1, buildId: "build", wasmSha256: digest(wasmBytes),
      exceptionTypeMapSha256: digest(mapBytes) }, mapBytes, wasmBytes };
  } });
  assert.deepEqual(await Promise.all([resolve(7), resolve(8)]), ["System.FormatException", null]);
  assert.equal(await resolve(7), "System.FormatException");
  assert.equal(loads, 1);
  for (const typeId of [0, -1, 1.5, 0x80000000]) await assert.rejects(resolve(typeId), TypeError);
  for (const loadArtifacts of [() => { throw new Error("missing"); }, () => ({}), () => null]) {
    const fallback = createManagedExceptionTypeResolver({ loadArtifacts, crypto: webcrypto });
    assert.equal(await fallback(7), null);
    assert.equal(await fallback(8), null);
  }
  assert.throws(() => createManagedExceptionTypeResolver({ loadArtifacts: null }), TypeError);
});
