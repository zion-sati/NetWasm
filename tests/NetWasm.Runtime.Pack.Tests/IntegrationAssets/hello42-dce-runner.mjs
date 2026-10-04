import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const [repository, target, manifestPath, modulePath, responsePath] = process.argv.slice(2);
assert.ok(repository && (target === "wasm32" || target === "wasm64"));
assert.ok(manifestPath && modulePath && responsePath);

const { instantiateNetWasm } = await import(
  pathToFileURL(`${repository}/src/NetWasm.Runtime/browser-host.mjs`));
const { createRuntimeContractImports } = await import(
  pathToFileURL(`${repository}/tests/NetWasm.Runtime.Tests/runtime-contract-imports.mjs`));
const { createOutputStreamImports } = await import(
  pathToFileURL(`${repository}/tests/NetWasm.Runtime.Tests/output-stream-imports.mjs`));

const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const module = await WebAssembly.compile(await readFile(modulePath));
const imports = WebAssembly.Module.imports(module);
assert(!imports.some(value => value.module === "wasi_snapshot_preview1" ||
  value.module === "wasi_unstable" || value.module === "netwasm.runtime.v1" ||
  value.module === "netwasm.application.v1"));

const width = target === "wasm64" ? 8 : 4;
const prefix = target === "wasm64" ? "cm64p2" : "cm32p2";
const stdout = [];
const stderr = [];
let managed;
const runtimeModules = createRuntimeContractImports(
  target,
  () => managed.instance.exports.memory,
);
runtimeModules[`${prefix}|wasi:cli/environment@0.2`] = {
  ...runtimeModules[`${prefix}|wasi:cli/environment@0.2`],
  "get-arguments"(result) {
    new Uint8Array(managed.instance.exports.memory.buffer, Number(result), width * 2).fill(0);
  },
};
Object.assign(runtimeModules, createOutputStreamImports(
  target,
  () => managed.instance.exports.memory,
  bytes => stdout.push(bytes),
  bytes => stderr.push(bytes),
));

managed = await instantiateNetWasm({ module, manifest, runtimeModules });
try {
  assert.equal(managed.instance.exports.run(), undefined);
} finally {
  managed.dispose();
}

const concatenate = chunks => {
  const result = new Uint8Array(chunks.reduce((length, chunk) => length + chunk.length, 0));
  let offset = 0;
  for (const chunk of chunks) {
    result.set(chunk, offset);
    offset += chunk.length;
  }
  return result;
};
const output = new TextDecoder().decode(concatenate(stdout));
const error = new TextDecoder().decode(concatenate(stderr));
assert.equal(output, "42\n");
assert.equal(error, "");
await writeFile(responsePath, JSON.stringify({ target, output, error, preview1Imports: 0 }));
