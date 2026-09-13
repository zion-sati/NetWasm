import assert from "node:assert/strict";
import test from "node:test";
import {
  bindNetWasmInterop,
  instantiateNetWasm,
  prepareNetWasmInterop,
  prepareRawNetWasmInterop,
} from "./browser-host.mjs";

const memoryModuleBytes = new Uint8Array([
  0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
  0x05, 0x03, 0x01, 0x00, 0x01,
  0x07, 0x0a, 0x01, 0x06, 0x6d, 0x65, 0x6d, 0x6f, 0x72, 0x79, 0x02, 0x00,
]);
const statusAbi = Object.freeze({
  successStatus: 0,
  hostFailureStatus: 1,
  scalarResultOffset: 0,
});
const targetLayout = Object.freeze({
  managedReferenceSize: 4,
  stringLengthOffset: 4,
  stringDataOffset: 8,
  arrayLengthOffset: 4,
  arrayDataPointerOffset: 8,
});
const descriptor = (name, result) => Object.freeze({
  module: "consumer",
  name,
  parameters: Object.freeze([]),
  result,
});
const manifest = imports => Object.freeze({
  version: 1,
  target: "wasm32",
  statusAbi,
  targetLayout,
  imports: Object.freeze(imports),
  exports: Object.freeze([]),
});

test("prepares status imports before instantiation and binds one real instance", async () => {
  const module = await WebAssembly.compile(memoryModuleBytes);
  const instance = await WebAssembly.instantiate(module, {});
  const hostObject = {};
  const preparation = prepareNetWasmInterop({
    manifest: manifest([descriptor("read_value", "i32"), descriptor("read_object", "object")]),
    runtimeModules: {},
    consumerModules: {
      consumer: {
        read_value() { return 42; },
        read_object() { return hostObject; },
      },
    },
  });

  assert.equal(Object.isFrozen(preparation), true);
  assert.equal(preparation.imports.consumer.read_value(0), statusAbi.hostFailureStatus);
  const boundary = bindNetWasmInterop({ preparation, instance });
  assert.equal(boundary.instance, instance);
  assert.equal(boundary.adapter.memory, instance.exports.memory);
  assert.equal(preparation.imports.consumer.read_value(0), statusAbi.successStatus);
  assert.equal(new DataView(instance.exports.memory.buffer).getInt32(0, true), 42);
  assert.equal(preparation.imports.consumer.read_object(4), statusAbi.successStatus);
  assert.equal(boundary.handles.count, 1);

  boundary.dispose();
  boundary.dispose();
  assert.equal(boundary.handles.count, 0);
  assert.equal(preparation.imports.consumer.read_value(0), statusAbi.hostFailureStatus);
});

test("keeps instantiateNetWasm as the supported delegating facade", async () => {
  const module = await WebAssembly.compile(memoryModuleBytes);
  const boundary = await instantiateNetWasm({
    module,
    manifest: manifest([]),
    runtimeModules: {},
  });

  assert.equal(boundary.instance instanceof WebAssembly.Instance, true);
  assert.equal(boundary.adapter.memory, boundary.instance.exports.memory);
  boundary.dispose();
});

test("adapts runtime interop to the raw execution strategy contract", async () => {
  const module = await WebAssembly.compile(memoryModuleBytes);
  const instance = await WebAssembly.instantiate(module, {});
  const rawInterop = prepareRawNetWasmInterop({
    manifest: manifest([descriptor("read_value", "i32")]),
    runtimeModules: {},
    consumerModules: { consumer: { read_value: () => 73 } },
  });

  assert.equal(Object.isFrozen(rawInterop), true);
  assert.deepEqual(Object.keys(rawInterop).sort(), ["bindInstance", "close", "imports"]);
  assert.equal(rawInterop.imports.consumer.read_value(0), statusAbi.hostFailureStatus);
  const boundary = rawInterop.bindInstance(instance);
  assert.equal(boundary.instance, instance);
  assert.equal(rawInterop.imports.consumer.read_value(0), statusAbi.successStatus);
  assert.equal(new DataView(instance.exports.memory.buffer).getInt32(0, true), 73);
  rawInterop.close();
  rawInterop.close();
  assert.equal(boundary.handles.count, 0);
  assert.equal(rawInterop.imports.consumer.read_value(0), statusAbi.hostFailureStatus);
});
