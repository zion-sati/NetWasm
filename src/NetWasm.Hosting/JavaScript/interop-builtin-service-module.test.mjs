import assert from "node:assert/strict";
import test from "node:test";

import { createInteropBuiltinServiceModule } from "./interop-builtin-service-module.mjs";
import { createInteropHandleTable } from "./interop-handle-table.mjs";

const layout = Object.freeze({
  managedReferenceSize: 4,
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
});

test("composes selected generated and supplied built-in services", async () => {
  const fixture = createFixture();
  const builtins = createInteropBuiltinServiceModule(fixture.request);

  assert.equal(builtins.custom(), 17);
  assert.equal(builtins.report_terminal_exception_v1(), 19);
  assert.notEqual(builtins.interop_string_length, fixture.overriddenStringLength);
  assert.deepEqual(fixture.statusDescriptors, ["queue_microtask"]);

  let callbacks = 0;
  builtins.queue_microtask(() => { callbacks++; });
  await Promise.resolve();
  assert.equal(callbacks, 1);
  const cancelled = builtins.queue_microtask(() => { callbacks++; });
  cancelled.dispose();
  await Promise.resolve();
  assert.equal(callbacks, 1);
});

test("adapts built-in string, byte, handle, and subscription services", () => {
  const fixture = createFixture();
  const builtins = createInteropBuiltinServiceModule(fixture.request);
  const view = new DataView(fixture.memory.buffer);

  const stringHandle = fixture.handles.acquire("abc");
  const objectHandle = fixture.handles.acquire({});
  assert.equal(builtins.interop_string_length(stringHandle), 3);
  assert.equal(builtins.interop_string_length(objectHandle), -1);
  assert.equal(builtins.interop_string_length(999), -1);
  view.setInt32(68, 3, true);
  assert.equal(builtins.interop_copy_string_utf16(stringHandle, 64), 0);
  assert.equal(new Uint16Array(fixture.memory.buffer, 72, 3).join(","), "97,98,99");
  assert.equal(builtins.interop_copy_string_utf16(objectHandle, 64), 1);
  assert.equal(builtins.interop_copy_string_utf16(999, 64), 1);

  const bytesHandle = fixture.handles.acquire(Uint8Array.of(4, 5, 6));
  assert.equal(builtins.interop_byte_length(bytesHandle), 3);
  assert.equal(builtins.interop_byte_length(objectHandle), -1);
  assert.equal(builtins.interop_byte_length(999), -1);
  view.setInt32(132, 3, true);
  view.setUint32(136, 160, true);
  assert.equal(builtins.interop_copy_bytes(bytesHandle, 128), 0);
  assert.deepEqual([...new Uint8Array(fixture.memory.buffer, 160, 3)], [4, 5, 6]);
  assert.equal(builtins.interop_copy_bytes(objectHandle, 128), 1);
  assert.equal(builtins.interop_copy_bytes(999, 128), 1);

  builtins.interop_release_handle(stringHandle);
  builtins.interop_release_handle(stringHandle);
  let disposed = 0;
  const subscription = fixture.handles.acquireSubscription(
    { dispose() { disposed++; } }, []);
  builtins.interop_release_subscription(subscription);
  builtins.interop_release_subscription(subscription);
  assert.equal(disposed, 1);
});

test("maps invalid managed destinations to built-in failure status", () => {
  const fixture = createFixture();
  const builtins = createInteropBuiltinServiceModule(fixture.request);
  const view = new DataView(fixture.memory.buffer);
  const stringHandle = fixture.handles.acquire("abc");
  view.setInt32(196, 2, true);
  assert.equal(builtins.interop_copy_string_utf16(stringHandle, 192), 1);
  const bytesHandle = fixture.handles.acquire(Uint8Array.of(1, 2));
  view.setInt32(260, 1, true);
  view.setUint32(264, 288, true);
  assert.equal(builtins.interop_copy_bytes(bytesHandle, 256), 1);
});

test("rejects malformed built-in service composition", () => {
  const { request } = createFixture();
  for (const value of [
    null,
    [],
    {},
    { ...request, extra: true },
    { ...request, [Symbol("invalid")]: true },
    Object.defineProperty({ ...request }, "contract", {
      enumerable: true, get: () => request.contract,
    }),
  ]) {
    assert.throws(() => createInteropBuiltinServiceModule(value), TypeError);
  }
  assert.throws(() => createInteropBuiltinServiceModule({
    ...request, createStatusService: null,
  }), /actions/);
  assert.throws(() => createInteropBuiltinServiceModule({
    ...request, readMemory: null,
  }), /actions/);
});

function createFixture() {
  const memory = new WebAssembly.Memory({ initial: 1 });
  const handles = createInteropHandleTable();
  const statusDescriptors = [];
  const overriddenStringLength = () => 23;
  const contract = Object.freeze({
    target: "wasm32",
    targetLayout: layout,
    imports: Object.freeze([
      Object.freeze({ module: "consumer", name: "unrelated" }),
      Object.freeze({ module: "netwasm.host.v1", name: "unknown" }),
      Object.freeze({ module: "netwasm.host.v1", name: "queue_microtask" }),
    ]),
  });
  return {
    memory,
    handles,
    overriddenStringLength,
    statusDescriptors,
    request: {
      builtinServices: {
        custom: () => 17,
        interop_string_length: overriddenStringLength,
      },
      contract,
      createStatusService(descriptor, service) {
        statusDescriptors.push(descriptor.name);
        return service;
      },
      exceptionReporter: { importObject: { report_terminal_exception_v1: () => 19 } },
      handles,
      readMemory: () => memory,
    },
  };
}
