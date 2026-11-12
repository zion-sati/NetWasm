import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRuntimeContractImports } from './runtime-contract-imports.mjs';
import { registerRuntimeTypes } from "../NetWasm.Compiler.Tests/Correctness/runtime-type-table.cjs";

const [modulePath, target] = process.argv.slice(2);
assert.ok(target === 'wasm32' || target === 'wasm64');
const address = value => target === 'wasm64' ? BigInt(value) : value;
const module = await WebAssembly.compile(fs.readFileSync(modulePath));
assert.equal(WebAssembly.Module.imports(module).some(entry =>
  entry.module === 'wasi_snapshot_preview1' || entry.module === 'wasi_unstable'), false);

for (const kind of ['root', 'value', 'stack-trace', 'root-content', 'value-content',
  'allocation-test-hooks', 'boxed-runtime']) {
  let api;
  const instance = await WebAssembly.instantiate(module,
    createRuntimeContractImports(target, () => api.memory));
  api = instance.exports;
  api.initialize(address(65536),
    kind === 'boxed-runtime' ? 3 : kind === 'allocation-test-hooks' ? 2 : 1,
    1);
  const before = BigInt(api.gc_get_metric(0));
  if (kind === 'boxed-runtime') {
    const referenceSize = target === 'wasm64' ? 8 : 4;
    const payloadOffset = referenceSize;
    const objectSize = referenceSize * 2;
    const view = new DataView(api.memory.buffer);
    // A boxed value with a reference in its payload, and the type its reference holds.
    registerRuntimeTypes(api, [
      { type: 1, size: objectSize, references: [1],
        value: { size: referenceSize, payload: payloadOffset, references: [0] } },
      { type: 2, size: referenceSize },
    ], address);

    const child = api.allocate(address(referenceSize), 2);
    const source = api.allocate(address(objectSize), 1);
    const writeReference = (location, value) => target === 'wasm64'
      ? view.setBigUint64(Number(location), BigInt(value), true)
      : view.setUint32(Number(location), Number(value), true);
    const readReference = location => target === 'wasm64'
      ? view.getBigUint64(Number(location), true)
      : BigInt(view.getUint32(Number(location), true));
    writeReference(BigInt(source) + BigInt(payloadOffset), child);

    const root = api.root_frame_enter(1);
    writeReference(root, source);
    api.test_collect_before_allocation(1);
    const clone = api.boxed_value_clone(source);
    api.test_collect_before_allocation(0);
    api.root_frame_leave(root);
    assert.ok(BigInt(api.gc_get_metric(0)) > before,
      'Box cloning must preserve its rooted source across a forced collection');

    assert.notEqual(BigInt(clone), 0n);
    assert.notEqual(BigInt(clone), BigInt(source));
    assert.equal(readReference(BigInt(clone) + BigInt(payloadOffset)), BigInt(child));
    assert.equal(api.runtime_helpers_equals(source, clone), 1);
    assert.equal(api.runtime_helpers_equals(source, source), 1);
    assert.equal(api.runtime_helpers_equals(address(0), source), 0);
    assert.equal(api.runtime_helpers_equals(source, address(0)), 0);
    writeReference(BigInt(clone) + BigInt(payloadOffset), address(0));
    assert.equal(api.runtime_helpers_equals(source, clone), 0);

    const firstReference = api.allocate(address(referenceSize), 2);
    const secondReference = api.allocate(address(referenceSize), 2);
    assert.equal(api.runtime_helpers_equals(firstReference, firstReference), 1);
    assert.equal(api.runtime_helpers_equals(firstReference, secondReference), 0);

    api.test_fail_next_allocation();
    assert.equal(BigInt(api.boxed_value_clone(source)), 0n);
    assert.notEqual(BigInt(api.boxed_value_clone(source)), 0n);
  } else if (kind === 'allocation-test-hooks') {
    registerRuntimeTypes(api, [{ type: 1, size: 8 }], address);
    api.test_track_allocations(1);
    assert.notEqual(BigInt(api.allocate(address(8), 1)), 0n);
    assert.equal(api.test_allocation_count(), 1);
    assert.equal(api.test_tracked_allocation_count(), 1);
    assert.equal(api.test_marked_count(), 1);

    api.test_track_allocations(0);
    const collectionsBeforeAllocation = api.test_collection_count();
    api.test_collect_before_allocation(1);
    assert.notEqual(BigInt(api.allocate(address(8), 1)), 0n);
    api.test_collect_before_allocation(0);
    assert.equal(api.test_collection_count(), collectionsBeforeAllocation + 1);

    api.test_fail_next_allocation();
    assert.equal(BigInt(api.allocate(address(8), 1)), 0n);
    assert.notEqual(BigInt(api.allocate(address(8), 1)), 0n);
    assert.equal(api.test_allocation_count(), 3);
  } else if (kind === 'root-content') {
    const slotCounts = [0, 1, 2, 3, 7, 8, 31, 32, 257, 1024, 1025];
    const invalidFrame = api.root_frame_enter(0);
    assert.notEqual(BigInt(invalidFrame), 0n);
    assert.throws(() => api.root_frame_leave(address(Number(invalidFrame) + 1)),
      WebAssembly.RuntimeError,
      'Leaving a non-current root frame address must trap');
    api.root_frame_leave(invalidFrame);
    for (let iteration = 0; iteration < 8; iteration++) {
      const frames = [];
      for (const slotCount of slotCounts) {
        const frame = api.root_frame_enter(slotCount);
        assert.notEqual(BigInt(frame), 0n);
        const slots = target === 'wasm64'
          ? new BigUint64Array(api.memory.buffer, Number(frame), slotCount)
          : new Uint32Array(api.memory.buffer, Number(frame), slotCount);
        const fill = BigInt(16 + iteration + slotCount);
        slots.fill(target === 'wasm64' ? fill : Number(fill));
        frames.push({ frame, slotCount, fill });
        assert.equal(api.test_shadow_depth(),
          frames.reduce((depth, active) => depth + active.slotCount, 0),
          'Diagnostic root depth must equal the aggregate active slot count');
        for (const active of frames) {
          const activeSlots = target === 'wasm64'
            ? new BigUint64Array(api.memory.buffer, Number(active.frame), active.slotCount)
            : new Uint32Array(api.memory.buffer, Number(active.frame), active.slotCount);
          assert.ok(activeSlots.every(value => BigInt(value) === active.fill),
            'Allocating another root frame must preserve existing frame contents');
        }
      }
      while (frames.length !== 0) api.root_frame_leave(frames.pop().frame);
      assert.equal(api.test_shadow_depth(), 0,
        'Leaving every root frame must restore an empty diagnostic depth');
    }
  } else if (kind === 'value-content') {
    const sizes = [0, 1, 2, 3, 7, 8, 15, 16, 17, 31, 32, 33,
      63, 64, 65, 127, 128, 129, 255, 256, 257,
      2047, 2048, 4095, 4096, 4097];
    const invalidFrame = api.value_frame_enter(address(0));
    assert.notEqual(BigInt(invalidFrame), 0n);
    assert.throws(() => api.value_frame_leave(address(Number(invalidFrame) + 1)),
      WebAssembly.RuntimeError,
      'Leaving a non-active value frame address must trap');
    api.value_frame_leave(invalidFrame);
    assert.throws(() => api.value_frame_leave(invalidFrame), WebAssembly.RuntimeError,
      'Leaving a value frame that was already left must trap');
    for (let iteration = 0; iteration < 8; iteration++) {
      const frames = [];
      for (const size of sizes) {
        const frame = api.value_frame_enter(address(size));
        assert.notEqual(BigInt(frame), 0n);
        const fill = 1 + (size + iteration) % 254;
        new Uint8Array(api.memory.buffer, Number(frame), size).fill(fill);
        frames.push({ frame, size, fill });
        for (const active of frames) {
          const bytes = new Uint8Array(api.memory.buffer, Number(active.frame), active.size);
          assert.ok(bytes.every(value => value === active.fill),
            'Allocating another value frame must preserve existing frame contents');
        }
      }
      if ((iteration & 1) !== 0) {
        const targetIndex = Math.floor(frames.length / 2);
        api.value_frame_leave(frames[targetIndex].frame);
        frames.splice(targetIndex);
        for (const active of frames) {
          const bytes = new Uint8Array(api.memory.buffer, Number(active.frame), active.size);
          assert.ok(bytes.every(value => value === active.fill),
            'Bulk unwinding nested value frames must preserve caller storage');
        }
      }
      while (frames.length !== 0) {
        const active = frames.pop();
        const bytes = new Uint8Array(api.memory.buffer, Number(active.frame), active.size);
        assert.ok(bytes.every(value => value === active.fill),
          'Leaving a nested value frame must preserve its caller storage');
        api.value_frame_leave(active.frame);
      }
    }
  } else if (kind === 'stack-trace') {
    for (let index = 0; index < 16384; index++) api.stack_trace_frame_enter(1);
    for (let index = 0; index < 16384; index++) api.stack_trace_frame_leave(1);
  } else {
    const frames = [];
    for (let index = 0; index < 64; index++) {
      const frame = kind === 'root'
        ? api.root_frame_enter(16384)
        : api.value_frame_enter(address(131072));
      assert.notEqual(BigInt(frame), 0n);
      frames.push(frame);
    }
    while (frames.length !== 0) api[`${kind}_frame_leave`](frames.pop());
  }
  const beforeFinalCollection = BigInt(api.gc_get_metric(0));
  if (kind !== 'allocation-test-hooks' && kind !== 'boxed-runtime') {
    assert.equal(beforeFinalCollection, before,
      'Metadata allocation must not introduce an unpublished managed collection point');
  }
  api.collect();
  assert.ok(BigInt(api.gc_get_metric(0)) > beforeFinalCollection,
    'Metadata allocation must restore the original collection state');
}
console.log(JSON.stringify({ target, passed: 7, failed: 0 }));
