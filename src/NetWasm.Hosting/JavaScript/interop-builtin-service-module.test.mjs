import assert from "node:assert/strict";
import test from "node:test";

import {
  createInteropBuiltinServiceModule,
  createJavaScriptValueServices,
} from "./interop-builtin-service-module.mjs";
import { createInteropHandleTable } from "./interop-handle-table.mjs";

const layout = Object.freeze({
  managedReferenceSize: 4,
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
  spanReferenceOffset: 0,
  spanLengthOffset: 4,
});

test("composes selected generated and supplied built-in services", async () => {
  const fixture = createFixture();
  const builtins = createInteropBuiltinServiceModule(fixture.request);

  assert.equal(builtins.custom(), 17);
  assert.equal(builtins.report_terminal_exception_v2(), 19);
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

  const numbersHandle = fixture.handles.acquire(Float64Array.of(1.5, -2));
  assert.equal(builtins.interop_byte_length(numbersHandle), 2);
  view.setInt32(132, 2, true);
  view.setUint32(136, 200, true);
  assert.equal(builtins.interop_copy_bytes(numbersHandle, 128), 0);
  assert.deepEqual([...new Float64Array(fixture.memory.buffer.slice(200, 216))], [1.5, -2]);

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
      exceptionReporter: { importObject: { report_terminal_exception_v2: () => 19 } },
      handles,
      readMemory: () => memory,
    },
  };
}

// CoreLib reads and builds JavaScript values through these, each behind a handle.
test("serves the JavaScript values CoreLib converts", () => {
  const handles = createInteropHandleTable();
  const registered = [];
  let collect;
  class Registry {
    constructor(callback) { collect = callback; }
    register(item, handle) { registered.push(handle); }
  }
  const values = createJavaScriptValueServices(handles, Registry);
  const kinds = [true, 1.5, 7n, "s", new Date(5), new Error("e"), [1], Uint8Array.of(1), Int32Array.of(1),
    Float64Array.of(1), {}];
  assert.equal(values.value_kind(0), 0);
  assert.equal(values.value_kind(values.value_element(handles.acquire([undefined]), 0)), 0);
  assert.deepEqual(kinds.map(item => values.value_kind(handles.acquire(item))), [1, 2, 3, 4, 5, 6, 7, 10, 11, 12, 13]);

  const managed = values.value_from_managed(5n);
  const managedError = values.value_from_managed_error("message", 6n);
  assert.deepEqual([values.value_kind(managed), values.value_kind(managedError)], [8, 9]);
  assert.deepEqual([values.value_managed(managed), values.value_managed(managedError)], [5n, 6n]);
  assert.equal(String(handles.get(managed)), "[object ManagedObject]");
  assert.deepEqual([handles.get(managedError).name, handles.get(managedError).message], ["ManagedError", "message"]);
  assert.deepEqual(registered, [5n, 6n]);
  assert.equal(values.value_collected(), 0n);
  collect(5n);
  assert.deepEqual([values.value_collected(), values.value_collected()], [5n, 0n]);

  assert.equal(values.value_number(handles.acquire(new Date(9))), 9);
  assert.equal(values.value_number(handles.acquire(true)), 1);
  assert.equal(values.value_bigint(handles.acquire(-1n)), -1n);
  assert.equal(values.value_string(handles.acquire(12)), "12");
  assert.equal(values.value_message(handles.acquire(new Error("boom"))), "boom");
  assert.equal(values.value_message(handles.acquire(3)), "3");
  const bytes = Uint8Array.of(2);
  for (const read of [values.value_bytes, values.value_int32s, values.value_doubles, values.value_object]) {
    assert.equal(read(handles.acquire(bytes)), bytes);
  }

  const array = values.value_new_array(2);
  assert.deepEqual(handles.get(array), [null, null]);
  values.value_set_element(array, 0, values.value_from_string("a"));
  values.value_set_element(array, 1, 0);
  assert.deepEqual(handles.get(array), ["a", null]);
  assert.equal(values.value_length(array), 2);
  assert.equal(handles.get(values.value_element(array, 0)), "a");
  assert.equal(values.value_element(array, 1), 0);
  const copy = values.value_copy(array);
  assert.equal(handles.get(copy), handles.get(array));
  assert.equal(values.value_copy(0), 0);
  const before = handles.count;
  values.value_release(copy);
  values.value_release(0);
  assert.equal(handles.count, before - 1);

  for (const [create, item] of [
    [values.value_from_number, 2], [values.value_from_boolean, false], [values.value_from_string, "t"],
    [values.value_from_bytes, bytes], [values.value_from_int32s, Int32Array.of(3)],
    [values.value_from_doubles, Float64Array.of(4)], [values.value_from_object, {}],
  ]) {
    assert.equal(handles.get(create(item)), item);
  }
  assert.equal(handles.get(values.value_from_date(5)).getTime(), 5);

  // Without a FinalizationRegistry, a managed object stays pinned.
  const pinned = createJavaScriptValueServices(handles, null);
  assert.equal(pinned.value_kind(pinned.value_from_managed(1n)), 8);
  assert.equal(pinned.value_collected(), 0n);
});