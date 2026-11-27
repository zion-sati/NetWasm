// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import {
  createInteropBuiltinServiceModule,
  createJavaScriptValueServices,
} from "./interop-builtin-service-module.mjs";
import { createInteropHandleTable } from "./interop-handle-table.mjs";
import { createJavaScriptErrorChannel } from "./javascript-error-channel.mjs";

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

// .NET's module-less [JSImport("globalThis.<path>")] calls the global function the path names.
test("serves a module-less import of a global function", () => {
  globalThis.netwasmBuiltinFixture = { offset: 5, shift(value) { return value + this.offset; } };
  try {
    const fixture = createFixture([
      Object.freeze({ module: "netwasm.host.v1", name: "globalThis.netwasmBuiltinFixture.shift" }),
      Object.freeze({ module: "netwasm.host.v1", name: "globalThis.netwasmBuiltinMissing" }),
    ]);
    const builtins = createInteropBuiltinServiceModule(fixture.request);

    assert.equal(builtins["globalThis.netwasmBuiltinFixture.shift"](2), 7);
    assert.equal(builtins["globalThis.netwasmBuiltinMissing"], undefined);
  } finally {
    delete globalThis.netwasmBuiltinFixture;
  }
});

// JSObject's property API reads and writes the object's own property, as .NET's browser runtime does.
test("serves JSObject's property access", () => {
  const names = [
    "object_has_property", "object_type_of_property", "object_get_boolean", "object_get_int32", "object_get_double",
    "object_get_string", "object_get_object", "object_get_bytes", "object_set_boolean", "object_set_int32",
    "object_set_double", "object_set_string", "object_set_object", "object_set_bytes",
  ];
  const fixture = createFixture(names.map(name => Object.freeze({ module: "netwasm.host.v1", name })));
  const builtins = createInteropBuiltinServiceModule(fixture.request);
  const target = { count: 3, nested: { a: 1 } };

  assert.equal(builtins.object_has_property(target, "count"), true);
  assert.equal(builtins.object_has_property(target, "missing"), false);
  assert.equal(builtins.object_has_property(target, "toString"), true);
  assert.equal(builtins.object_type_of_property(target, "nested"), "object");
  assert.equal(builtins.object_type_of_property(target, "missing"), "undefined");
  for (const name of names.filter(name => name.startsWith("object_get_"))) {
    assert.equal(builtins[name](target, "count"), 3, name);
  }
  for (const [index, name] of names.filter(name => name.startsWith("object_set_")).entries()) {
    assert.equal(builtins[name](target, "value" + index, index), undefined, name);
    assert.equal(target["value" + index], index, name);
  }
  assert.throws(() => builtins.object_get_int32(null, "count"), TypeError);
});

// JSHost.GlobalThis is the global object, and JSHost.ImportAsync completes with a module the host bound.
test("serves the global object and the host's bound modules", () => {
  const bound = { greet() {} };
  const fixture = createFixture(
    ["host_global_this", "host_module"].map(name => Object.freeze({ module: "netwasm.host.v1", name })),
    Object.freeze({ "app.bound": bound }));
  const builtins = createInteropBuiltinServiceModule(fixture.request);

  assert.strictEqual(builtins.host_global_this(), globalThis);
  assert.strictEqual(builtins.host_module("app.bound"), bound);
  assert.equal(builtins.host_module("app.missing"), null);
  assert.equal(builtins.host_module("toString"), null);
});

test("adapts built-in string, handle, and subscription services", () => {
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

  // Number arrays enter managed code staged in linear memory, not through these services.
  assert.equal(builtins.interop_byte_length, undefined);
  assert.equal(builtins.interop_copy_bytes, undefined);

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

function createFixture(additionalImports = [], consumerModules = {}) {
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
      ...additionalImports,
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
      consumerModules,
      contract,
      createStatusService(descriptor, service) {
        statusDescriptors.push(descriptor.name);
        return service;
      },
      errors: createJavaScriptErrorChannel(handles),
      exceptionReporter: { importObject: { report_terminal_exception_v2: () => 19 } },
      handles,
      readMemory: () => memory,
    },
  };
}

// CoreLib reads and builds JavaScript values through these, each behind a handle.
// JavaScript can collect many managed objects at once; CoreLib takes each back in turn, which
// shifting the queue made cost time in proportion to those left.
test("taking a collected handle costs the same however many wait", () => {
  let collect;
  class Registry {
    constructor(callback) { collect = callback; }
    register() {}
  }
  const values = createJavaScriptValueServices(createInteropHandleTable(), Registry);
  const count = 200_000;
  for (let handle = 1; handle <= count; handle++) collect(BigInt(handle));
  const start = performance.now();
  let total = 0n;
  for (let handle = values.value_collected(); handle !== 0n; handle = values.value_collected()) total += handle;
  assert.ok(performance.now() - start < 1000, `took ${performance.now() - start} ms`);
  assert.equal(total, BigInt(count) * BigInt(count + 1) / 2n);
});

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
  assert.equal(values.value_message(handles.acquire(new Error("boom"))), "Error: boom");
  assert.equal(values.value_message(handles.acquire(new DOMException("no", "AbortError"))), "AbortError: no");
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

  // An ArraySegment's view keeps its array's handle until JavaScript disposes or collects it.
  const memory = new WebAssembly.Memory({ initial: 1 });
  new Uint8Array(memory.buffer).set([5, 6, 7], 16);
  const unregistered = [];
  class ViewRegistry {
    constructor(callback) { this.callback = callback; }
    register(item, handle, token) { registered.push([handle, token === item]); }
    unregister(token) { unregistered.push(token); }
  }
  const viewValues = createJavaScriptValueServices(handles, ViewRegistry, () => memory);
  const viewHandle = viewValues.value_from_memory_view(8n, 17, 2, 0);
  const view = handles.get(viewHandle);
  assert.deepEqual([...view.slice()], [6, 7]);
  assert.deepEqual(registered.at(-1), [8n, true]);
  assert.equal(viewValues.value_collected(), 0n);
  view.dispose();
  assert.deepEqual(unregistered, [view]);
  assert.equal(viewValues.value_collected(), 8n);
  const unregisteredView = createJavaScriptValueServices(handles, null, () => memory);
  handles.get(unregisteredView.value_from_memory_view(9n, 16, 1, 0)).dispose();
  assert.equal(unregisteredView.value_collected(), 9n);

  // Without a FinalizationRegistry, a managed object stays pinned.
  const pinned = createJavaScriptValueServices(handles, null);
  assert.equal(pinned.value_kind(pinned.value_from_managed(1n)), 8);
  assert.equal(pinned.value_collected(), 0n);
});
// A managed exception that crossed to JavaScript and is a failed import's pending error comes back as itself.
test("reports a pending ManagedError's managed handle", () => {
  const handles = createInteropHandleTable();
  const errors = createJavaScriptErrorChannel(handles);
  const values = createJavaScriptValueServices(handles, null, null, errors);
  const crossing = values.value_from_managed_error("managed", 42n);
  errors.captureThrow(handles.get(crossing));
  assert.equal(values.error_pending_managed(), 42n);
  errors.services.error_take();
  errors.captureThrow(new Error("plain"));
  assert.equal(values.error_pending_managed(), 0n);
});
