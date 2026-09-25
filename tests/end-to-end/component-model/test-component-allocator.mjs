import { readFile } from "node:fs/promises";
import assert from "node:assert/strict";
import { createRuntimeContractImports } from "../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";

const [runtimePath, target = "wasm32"] = process.argv.slice(2);
if (!runtimePath || !new Set(["wasm32", "wasm64"]).has(target)) {
    throw new Error("usage: test-component-allocator.mjs <runtime.wasm> [wasm32|wasm64]");
}

const address = target === "wasm64" ? value => BigInt(value) : value => value;
const maximumAddress = target === "wasm64" ? 0xffff_ffff_ffff_ffffn : 0xffff_ffff;
let runtime;
let environmentReads = 0;
const imports = createRuntimeContractImports(target, () => {
    ++environmentReads;
    return runtime.memory;
});
const bytes = await readFile(runtimePath);
const module = await WebAssembly.compile(bytes);
assert.equal(WebAssembly.Module.imports(module).some(entry =>
    entry.module === "wasi_snapshot_preview1" || entry.module === "wasi_unstable"), false);
const instance = await WebAssembly.instantiate(module, imports);
runtime = instance.exports;
runtime.initialize(address(65536), 1, 0);
assert.equal(environmentReads, 1);

const first = runtime.component_realloc(address(0), address(0), address(1), address(18));
const second = runtime.component_realloc(address(0), address(0), address(8), address(8));
if (first === address(0) || second === address(0) || first === second) {
    throw new Error("canonical allocator returned an invalid allocation");
}
if (runtime.test_component_active_allocation_count() !== 2) {
    throw new Error("canonical allocator did not track two live allocations");
}
if (Number(second) % 8 !== 0) {
    throw new Error("canonical allocator did not honor 8-byte alignment");
}

const memory = new Uint8Array(runtime.memory.buffer);
for (let index = 0; index < 18; index++) {
    memory[Number(first) + index] = index + 1;
}
const grown = runtime.component_realloc(first, address(18), address(8), address(36));
if (grown === address(0) || Number(grown) % 8 !== 0) {
    throw new Error("canonical allocator could not grow an allocation");
}
const grownMemory = new Uint8Array(runtime.memory.buffer);
if (runtime.test_component_active_allocation_count() !== 2) {
    throw new Error("canonical realloc changed the live allocation count");
}
for (let index = 0; index < 18; index++) {
    if (grownMemory[Number(grown) + index] !== index + 1) {
        throw new Error(`canonical realloc lost byte ${index}`);
    }
}

// A request larger than all current linear memory must grow memory, not merely
// resize a block within an existing heap. Keep earlier allocations live.
const oldBuffer = runtime.memory.buffer;
const largeLength = oldBuffer.byteLength + 65536;
const large = runtime.component_realloc(address(0), address(0), address(8), address(largeLength));
assert.notEqual(large, address(0));
assert.equal(Number(large) % 8, 0);
assert.notEqual(runtime.memory.buffer, oldBuffer);
assert.equal(oldBuffer.byteLength, 0);
assert.equal(runtime.test_component_active_allocation_count(), 3);
const refreshedMemory = new Uint8Array(runtime.memory.buffer);
for (let index = 0; index < 18; index++) {
    assert.equal(refreshedMemory[Number(grown) + index], index + 1);
}
refreshedMemory[Number(large)] = 17;
refreshedMemory[Number(large) + largeLength - 1] = 239;
assert.equal(refreshedMemory[Number(large)], 17);
assert.equal(refreshedMemory[Number(large) + largeLength - 1], 239);
runtime.component_free(large);
assert.equal(runtime.test_component_active_allocation_count(), 2);

runtime.component_free(second);
runtime.component_realloc(grown, address(36), address(8), address(0));
const afterFree = runtime.component_realloc(
    address(0), address(0), address(4), address(4));
if (afterFree === address(0)) {
    throw new Error("canonical allocator did not remain usable after free");
}
runtime.component_free(afterFree);
if (runtime.test_component_active_allocation_count() !== 0) {
    throw new Error("canonical allocations leaked after deterministic cleanup");
}

runtime.test_fail_next_component_allocation();
const failed = runtime.component_realloc(
    address(0), address(0), address(1), address(8));
if (failed !== address(0) || runtime.test_component_active_allocation_count() !== 0) {
    throw new Error("canonical allocation failure was not atomic");
}
const oversized = runtime.component_realloc(
    address(0), address(0), address(1), maximumAddress);
if (oversized !== address(0) || runtime.test_component_active_allocation_count() !== 0) {
    throw new Error("oversized canonical allocation did not fail atomically");
}
let rejectedAlignment = false;
try {
    runtime.component_realloc(address(0), address(0), address(3), address(8));
} catch (error) {
    rejectedAlignment = error instanceof WebAssembly.RuntimeError;
}
if (!rejectedAlignment || runtime.test_component_active_allocation_count() !== 0) {
    throw new Error("canonical allocator accepted an invalid alignment");
}

console.log(`Canonical allocator ${target} acceptance PASS`);
