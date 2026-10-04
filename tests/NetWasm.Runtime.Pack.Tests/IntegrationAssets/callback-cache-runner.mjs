import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const [repository, target, manifestPath, modulePath, responsePath, callbackName] =
  process.argv.slice(2);
assert.ok(repository && (target === "wasm32" || target === "wasm64"));
assert.ok(manifestPath && modulePath && responsePath && callbackName);

const { instantiateNetWasm } = await import(
  pathToFileURL(`${repository}/src/NetWasm.Runtime/browser-host.mjs`));
const { createRuntimeContractImports } = await import(
  pathToFileURL(`${repository}/tests/NetWasm.Runtime.Tests/runtime-contract-imports.mjs`));
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const module = await WebAssembly.compile(await readFile(modulePath));
const imports = WebAssembly.Module.imports(module);
assert(!imports.some(value => value.module === "wasi_snapshot_preview1" ||
  value.module === "wasi_unstable" || value.module === "netwasm.runtime.v1" ||
  value.module === "netwasm.application.v1"));

let managed;
const events = [];
managed = await instantiateNetWasm({
  module,
  manifest,
  runtimeModules: createRuntimeContractImports(
    target,
    () => managed.instance.exports.memory,
  ),
  managedExceptionReporting: { reportImmediate: event => events.push(event) },
});
try {
  assert.throws(() => managed.instance.exports[callbackName](11), WebAssembly.RuntimeError);
  assert.equal(events.length, 0);
  assert.equal(managed.instance.exports.run(0), 0);
  assert.equal(managed.instance.exports[callbackName](11), 42);
  assert.equal(managed.instance.exports.run(0), 101);
  assert.equal(events.length, 0);
} finally {
  managed.dispose();
}

await writeFile(responsePath, JSON.stringify({
  target,
  callbackName,
  result: 42,
  state: 101,
  fatalReports: events.length,
  preview1Imports: 0,
  nativeHostFallbacks: 0,
}));
