import assert from "node:assert/strict";
import fs from "node:fs";
import { instantiateNetWasm } from "../../../../src/NetWasm.Runtime/browser-host.mjs";
import { createRuntimeContractImports } from "../../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";

const [modulePath, manifestPath, target] = process.argv.slice(2);
assert.ok(target === "wasm32" || target === "wasm64");
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
assert.equal(manifest.target, target);
const module = await WebAssembly.compile(fs.readFileSync(modulePath));
assert.ok(WebAssembly.Module.imports(module).every(({ module }) =>
  module !== "wasi_snapshot_preview1" && module !== "wasi_unstable"));

async function createInstance() {
  let managed;
  let growths = 0;
  const grow = () => {
    const memory = managed.instance.exports.memory;
    const previous = memory.buffer;
    const length = previous.byteLength;
    memory.grow(target === "wasm64" ? 1n : 1);
    assert.notEqual(memory.buffer, previous);
    assert.equal(previous.byteLength, 0);
    assert.equal(memory.buffer.byteLength, length + 65536);
    growths++;
  };
  managed = await instantiateNetWasm({
    module,
    manifest,
    runtimeModules: createRuntimeContractImports(target, () => managed.instance.exports.memory),
    consumerModules: {
      "consumer.memory": {
        grow_text(value) {
          assert.ok(value === null || value === "" || value === "a\0Ω\ud800\u{1F31E}");
          grow();
          return value === null ? null : value + "!";
        },
        grow_bytes(value) {
          assert.ok(value === null || value instanceof Uint8Array);
          // Copy before growth: the imported buffer may itself view guest memory.
          const snapshot = value === null ? null : [...value];
          assert.ok(snapshot === null || snapshot.length === 0 ||
            JSON.stringify(snapshot) === "[0,127,128,255]");
          grow();
          return snapshot === null ? null : new Uint8Array([...snapshot, 42]);
        },
      },
    },
  });
  return { managed, growths: () => growths };
}

const first = await createInstance();
const second = await createInstance();
assert.notEqual(first.managed.instance.exports.memory, second.managed.instance.exports.memory);
assert.equal(first.managed.exports.read_state(0), -1);
assert.equal(second.managed.exports.read_state(0), -1);
assert.equal(first.managed.exports.initialize_state(11), 1);
assert.equal(second.managed.exports.initialize_state(99), 1);
assert.equal(first.managed.exports.advance_state(2), 13);
assert.equal(second.managed.exports.read_state(0), 99);
assert.equal(second.managed.exports.advance_state(-1), 98);
assert.equal(first.managed.exports.read_state(0), 13);

for (const state of [first, second]) {
  for (const mode of [0, 1, 2]) {
    assert.equal(state.managed.exports.check_text(mode), 42);
    assert.equal(state.managed.exports.collect_and_check(0), 42);
    assert.equal(state.managed.exports.check_bytes(mode), 42);
    assert.equal(state.managed.exports.collect_and_check(0), 42);
  }
  assert.equal(state.growths(), 6);
  assert.equal(state.managed.handles.count, 0);
}
assert.equal(first.managed.exports.read_state(0), 13);
assert.equal(second.managed.exports.read_state(0), 98);
first.managed.dispose();
assert.equal(second.managed.exports.advance_state(2), 100);
assert.equal(second.managed.exports.check_text(2), 42);
assert.equal(second.managed.exports.collect_and_check(0), 42);
assert.equal(second.growths(), 7);
second.managed.dispose();
assert.equal(first.managed.handles.count, 0);
assert.equal(second.managed.handles.count, 0);
process.stdout.write(`Instance isolation and memory growth ${target} PASS\n`);
