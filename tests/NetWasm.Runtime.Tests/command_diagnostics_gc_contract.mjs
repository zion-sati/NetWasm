import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createRuntimeContractImports } from './runtime-contract-imports.mjs';

const [modulePath, target] = process.argv.slice(2);
assert.ok(target === 'wasm32' || target === 'wasm64');

const memory64 = target === 'wasm64';
const address = value => memory64 ? BigInt(value) : value;
const numeric = value => Number(value);
const module = await WebAssembly.compile(fs.readFileSync(modulePath));
let api;
const instance = await WebAssembly.instantiate(module,
  createRuntimeContractImports(target, () => api.memory));
api = instance.exports;

api.initialize(address(65536), 2, 0);
const referenceSize = memory64 ? 8 : 4;
const stringObjectSize = memory64 ? 16 : 8;
const stringDataOffset = memory64 ? 12 : 8;
api.register_type(1, 0, address(stringObjectSize), address(0), 0, address(0), 0, 0, 0);

const roots = api.root_frame_enter(2);
const rootView = new DataView(api.memory.buffer);
const message = api.allocate_string('M'.charCodeAt(0), 3, 1);
if (memory64) rootView.setBigUint64(numeric(roots), message, true);
else rootView.setUint32(numeric(roots), message, true);
const stackTrace = api.allocate_string('S'.charCodeAt(0), 4, 1);
if (memory64) rootView.setBigUint64(numeric(roots) + referenceSize, stackTrace, true);
else rootView.setUint32(numeric(roots) + referenceSize, stackTrace, true);

const messageWeak = api.weak_handle_new(message, 0);
const stackTraceWeak = api.weak_handle_new(stackTrace, 0);
api.command_exception_capture(7, message, 3, stackTrace, 4);
api.root_frame_leave(roots);
api.collect();
assert.notEqual(BigInt(api.weak_handle_get(messageWeak)), 0n);
assert.notEqual(BigInt(api.weak_handle_get(stackTraceWeak)), 0n);

const completion = numeric(api.command_exception_completion(0));
const view = new DataView(api.memory.buffer);
const eventOffset = memory64 ? 8 : 4;
const messageOffset = memory64 ? 16 : 8;
const stackTraceOffset = memory64 ? 40 : 20;
const pointerOffset = memory64 ? 8 : 4;
const readAddress = offset => memory64
  ? view.getBigUint64(offset, true)
  : BigInt(view.getUint32(offset, true));
assert.equal(view.getUint8(completion), 1);
assert.equal(view.getUint32(completion + eventOffset, true), 7);
assert.equal(view.getUint8(completion + messageOffset), 1);
assert.equal(readAddress(completion + messageOffset + pointerOffset),
  BigInt(message) + BigInt(stringDataOffset));
assert.equal(readAddress(completion + messageOffset + pointerOffset * 2), 3n);
assert.equal(view.getUint8(completion + stackTraceOffset), 1);
assert.equal(readAddress(completion + stackTraceOffset + pointerOffset),
  BigInt(stackTrace) + BigInt(stringDataOffset));
assert.equal(readAddress(completion + stackTraceOffset + pointerOffset * 2), 4n);

api.command_exception_release();
api.collect();
assert.equal(BigInt(api.weak_handle_get(messageWeak)), 0n);
assert.equal(BigInt(api.weak_handle_get(stackTraceWeak)), 0n);
api.weak_handle_release(messageWeak);
api.weak_handle_release(stackTraceWeak);

console.log(JSON.stringify({ target, passed: 1, failed: 0 }));
