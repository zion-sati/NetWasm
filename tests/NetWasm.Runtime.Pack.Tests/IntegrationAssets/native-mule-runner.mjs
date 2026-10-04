import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const [repository, target, manifestPath, modulePath, responsePath, internalExportsPath, profile = "ordinary"] = process.argv.slice(2);
const { instantiateNetWasm } = await import(pathToFileURL(`${repository}/src/NetWasm.Runtime/browser-host.mjs`));
const { createRuntimeContractImports } = await import(pathToFileURL(`${repository}/tests/NetWasm.Runtime.Tests/runtime-contract-imports.mjs`));
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const module = await WebAssembly.compile(await readFile(modulePath));
const imports = WebAssembly.Module.imports(module);
const internalExports = JSON.parse(await readFile(internalExportsPath, "utf8"));
const exports = WebAssembly.Module.exports(module);
assert(internalExports.length > 0);
assert(!internalExports.some(internal => exports.some(value => value.name === internal.Name)));
assert(!imports.some(value => value.module === "wasi_snapshot_preview1" || value.module === "wasi_unstable"));
assert(!imports.some(value => value.module === "mule" || value.module === "unused" ||
  value.module === "netwasm.runtime.v1" || value.module === "netwasm.application.v1"));
const observations = [];
const growth = [];
let fatalReports = 0;
for (let index = 0; index < 2; index++) {
  let managed;
  const events = [];
  managed = await instantiateNetWasm({
    module,
    manifest,
    runtimeModules: createRuntimeContractImports(target, () => managed.instance.exports.memory),
    managedExceptionReporting: { reportImmediate: event => events.push(event) },
  });
  try {
    const initialMemoryBytes = managed.instance.exports.memory.buffer.byteLength;
    let values;
    if (profile === "callback-only") {
      assert.throws(
        () => managed.instance.exports.native_mule_callback_only(-100),
        WebAssembly.RuntimeError,
      );
      assert.equal(events.length, 0);
      assert.equal(managed.instance.exports.run(0), 0);
      values = [
        managed.instance.exports.native_mule_callback_only(-100),
        managed.instance.exports.native_mule_callback_only(35),
      ];
      assert.deepEqual(values, [1, 137]);
      assert.equal(managed.instance.exports.run(0), 102);
    } else if (profile === "callback-failure") {
      const expectedPreparation = index === 0 ? 0 : 102;
      assert.equal(managed.instance.exports.run(index), expectedPreparation);
      assert.equal(events.length, 0);
      assert.throws(
        () => managed.instance.exports.native_mule_callback_failure(11),
        WebAssembly.RuntimeError,
      );
      assert.equal(events.length, 1);
      assert.equal(events[0].message, "callback initializer first attempt");
      fatalReports += events.length;
      values = [expectedPreparation];
    } else {
      values = [managed.instance.exports.run(-100), managed.instance.exports.run(35)];
      assert.deepEqual(values, profile === "named-callbacks" ? [-100, 35] : [42, 42]);
    }
    if (profile !== "callback-failure") assert.equal(events.length, 0);
    const finalMemoryBytes = managed.instance.exports.memory.buffer.byteLength;
    if (profile === "named-callbacks" || profile === "callback-only") {
      assert.equal(finalMemoryBytes, initialMemoryBytes);
    } else if (profile !== "callback-failure") {
      assert(finalMemoryBytes > initialMemoryBytes);
    }
    observations.push(values);
    growth.push({ initialMemoryBytes, finalMemoryBytes });
  } finally {
    managed.dispose();
  }
}
if (profile === "callbacks") {
  let managed;
  const events = [];
  managed = await instantiateNetWasm({
    module,
    manifest,
    runtimeModules: createRuntimeContractImports(target, () => managed.instance.exports.memory),
    managedExceptionReporting: { reportImmediate: event => events.push(event) },
  });
  try {
    assert.throws(() => managed.instance.exports.run(0x7fffffff));
    assert.equal(events.length, 1);
    fatalReports = events.length;
  } finally {
    managed.dispose();
  }
}
await writeFile(responsePath, JSON.stringify({ target, observations, growth, fatalReports,
  preview1Imports: 0, nativeHostFallbacks: 0 }));
