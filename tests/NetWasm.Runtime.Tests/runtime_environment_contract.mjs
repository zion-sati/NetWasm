import assert from 'node:assert/strict';
import fs from 'node:fs';

const [modulePath, target, collectionMode = 'normal'] = process.argv.slice(2);
assert.ok(modulePath);
assert.ok(target === 'wasm32' || target === 'wasm64');
assert.ok(collectionMode === 'normal' || collectionMode === 'forced');
const addressSize = target === 'wasm64' ? 8 : 4;
const address = value => addressSize === 8 ? BigInt(value) : Number(value);
const prefix = addressSize === 8 ? 'cm64p2' : 'cm32p2';
const module = await WebAssembly.compile(fs.readFileSync(modulePath));
assert.equal(WebAssembly.Module.imports(module).some(entry =>
  /wasi_snapshot_preview1|wasi_unstable/.test(entry.module)), false);
const cases = [
  { environment: {}, disabled: false },
  { environment: { GC_DONT_GC_EXTRA: '1', GC_DONT_G: '1' }, disabled: false },
  { environment: { gc_dont_gc: '1' }, disabled: false },
  { environment: { BEFORE: 'value', GC_DONT_GC: '1', AFTER: 'value' }, disabled: true },
  { environment: { GC_DONT_GC: '' }, disabled: true },
  { environment: { GC_DONT_GC: '0' }, disabled: true },
  { environment: { UNICODE: 'caf\u00e9=\u6c34', EMPTY: '', GC_DONT_GC: '1' }, disabled: true },
];

const startupCases = cases.flatMap(entry => [
  { ...entry, earlyAllocation: false },
  { ...entry, earlyAllocation: true },
]);
for (const { environment, disabled, earlyAllocation } of startupCases) {
  const encoded = Object.entries(environment).map(([key, value]) =>
    [new TextEncoder().encode(key), new TextEncoder().encode(value)]);
  let instance;
  let valueReads = 0;
  let stderrReads = 0;
  const output = [];
  function writeSize(address, value) {
    const view = new DataView(instance.exports.memory.buffer);
    if (addressSize === 8) view.setBigUint64(Number(address), BigInt(value), true);
    else view.setUint32(Number(address), value, true);
  }
  function unexpected() {
    assert.fail('Unexpected runtime host call');
  }
  function allocate(size, alignment) {
    if (size === 0) return 0;
    const pointer = instance.exports.component_realloc(
      address(0), address(0), address(alignment), address(size));
    assert.notEqual(pointer, address(0));
    return Number(pointer);
  }
  instance = await WebAssembly.instantiate(module, {
    'netwasm.application.v1': {
      'netwasm.filter': unexpected,
      'netwasm.finalize': unexpected,
    },
    env: { emscripten_notify_memory_growth() {} },
    [`${prefix}|wasi:cli/environment@0.2`]: {
      'get-environment'(result) {
        ++valueReads;
        const pointers = allocate(encoded.length * addressSize * 4, addressSize);
        for (let index = 0; index < encoded.length; ++index) {
          for (let field = 0; field < 2; ++field) {
            const bytes = encoded[index][field];
            const pointer = allocate(bytes.length, 1);
            if (bytes.length !== 0) {
              new Uint8Array(instance.exports.memory.buffer).set(bytes, pointer);
            }
            const offset = pointers + (index * 4 + field * 2) * addressSize;
            writeSize(offset, pointer);
            writeSize(offset + addressSize, bytes.length);
          }
        }
        writeSize(result, pointers);
        writeSize(Number(result) + addressSize, encoded.length);
      },
    },
    [`${prefix}|wasi:cli/stdout@0.2`]: { 'get-stdout': unexpected },
    [`${prefix}|wasi:cli/stderr@0.2`]: {
      'get-stderr'() { ++stderrReads; return 42; },
    },
    [`${prefix}|wasi:io/streams@0.2`]: {
      '[method]output-stream.blocking-write-and-flush'(handle, contents, length, result) {
        assert.equal(handle, 42);
        assert.ok(Number(length) <= 4096);
        output.push(new TextDecoder().decode(new Uint8Array(
          instance.exports.memory.buffer, Number(contents), Number(length))));
        new DataView(instance.exports.memory.buffer).setUint8(Number(result), 0);
      },
      'output-stream_drop': unexpected,
    },
    [`${prefix}|wasi:io/error@0.2`]: { error_drop: unexpected },
    [`${prefix}|wasi:cli/exit@0.2`]: { exit: unexpected },
  });
  let early;
  if (earlyAllocation) {
    assert.doesNotThrow(() => { early = allocate(19, 16); },
      'canonical allocation before initialization');
    new Uint8Array(instance.exports.memory.buffer, early, 19).fill(0x5a);
  }
  assert.equal(instance.exports.test_component_active_allocation_count(), Number(earlyAllocation));
  assert.equal(valueReads, 0);
  const staticDataEnd = address(65536);
  assert.doesNotThrow(() => instance.exports.initialize(staticDataEnd, 1, 1),
    'runtime initialization through Preview 2');
  assert.equal(valueReads, 1);
  assert.equal(instance.exports.test_component_active_allocation_count(), Number(earlyAllocation));
  if (earlyAllocation) {
    assert.deepEqual([...new Uint8Array(instance.exports.memory.buffer, early, 19)],
      Array(19).fill(0x5a));
  }
  const collectionsBeforeLateAllocation = instance.exports.gc_get_metric(0);
  const late = allocate(29, 16);
  if (collectionMode === 'forced' && !disabled) {
    assert.ok(BigInt(instance.exports.gc_get_metric(0)) > BigInt(collectionsBeforeLateAllocation));
  }
  assert.equal(instance.exports.test_component_active_allocation_count(), 1 + Number(earlyAllocation));
  new Uint8Array(instance.exports.memory.buffer, late, 29).fill(0xc3);
  instance.exports.collect();
  assert.deepEqual([...new Uint8Array(instance.exports.memory.buffer, late, 29)],
    Array(29).fill(0xc3));
  if (earlyAllocation) {
    assert.deepEqual([...new Uint8Array(instance.exports.memory.buffer, early, 19)],
      Array(19).fill(0x5a));
  }
  if (earlyAllocation) instance.exports.component_free(address(early));
  instance.exports.component_free(address(late));
  assert.equal(instance.exports.test_component_active_allocation_count(), 0);
  const before = instance.exports.gc_get_metric(0);
  instance.exports.collect();
  const after = instance.exports.gc_get_metric(0);
  if (disabled) {
    assert.equal(BigInt(before), 0n);
    assert.equal(BigInt(after), 0n);
  } else {
    assert.ok(BigInt(after) > BigInt(before));
  }
  instance.exports.initialize(staticDataEnd, 1, 1);
  assert.equal(valueReads, 1);
  instance.exports.report_unobserved_task_exception();
  instance.exports.report_unobserved_task_exception();
  assert.equal(stderrReads, 1);
  assert.deepEqual(output, Array(2).fill(
    'NetWasm: an unobserved managed Task exception was finalized\n'));
}
console.log(JSON.stringify({ target, collectionMode, passed: startupCases.length, failed: 0 }));
