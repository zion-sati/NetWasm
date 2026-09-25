import { readFile } from "node:fs/promises";
import { createRuntimeContractImports } from "../../NetWasm.Runtime.Tests/runtime-contract-imports.mjs";

var [modulePath, target, profile, applicationPath, runtimePath] = process.argv.slice(2);
if (!modulePath || !new Set(["wasm32", "wasm64"]).has(target) ||
    !new Set(["pure", "clock"]).has(profile) || !applicationPath || !runtimePath) {
  throw new Error(
    "usage: run-trimming.mjs <module.wasm> <wasm32|wasm64> <pure|clock> <application.wasm> <runtime.wasm>");
}

var bytes = await readFile(modulePath);
var module = await WebAssembly.compile(bytes);
var imports = WebAssembly.Module.imports(module);
var application = await WebAssembly.compile(await readFile(applicationPath));
var runtime = await WebAssembly.compile(await readFile(runtimePath));
for (var candidate of [module, application, runtime]) {
  if (WebAssembly.Module.imports(candidate).some(({ module }) =>
      module === "wasi_snapshot_preview1" || module === "wasi_unstable"))
    throw new Error("Preview 1 imports are forbidden");
}
var canonicalPrefix = target === "wasm64" ? "cm64p2" : "cm32p2";
var isPublicImport = value =>
  value.module.startsWith("cm32p2|") || value.module.startsWith("cm64p2|") ||
  value.module.startsWith("consumer.");
var publicImports = WebAssembly.Module.imports(application).filter(isPublicImport);
var expected = profile === "clock"
  ? [`${canonicalPrefix}|wasi:clocks/wall-clock@0.2#now`]
  : [];
var actual = publicImports.map(value => `${value.module}#${value.name}`).sort();
if (JSON.stringify(actual) !== JSON.stringify(expected)) {
  throw new Error(
    `${profile} retained unexpected public imports: ${JSON.stringify(actual)}`);
}
var runtimeImports = WebAssembly.Module.imports(runtime).filter(isPublicImport)
  .map(value => `${value.module}#${value.name}`);
var allowed = new Set([...expected, ...runtimeImports]);
var finalImports = imports.filter(isPublicImport).map(value => `${value.module}#${value.name}`);
if (finalImports.some(value => !allowed.has(value)) || expected.some(value => !finalImports.includes(value)))
  throw new Error("linked module changed the application/native import contract");

var activeMemory;
var address = value => typeof value === "bigint" ? Number(value) : value;
var instance = await WebAssembly.instantiate(module, {
  ...createRuntimeContractImports(target, () => instance.exports.memory),
  "netwasm.host.v1": {
    write_i32() {},
    report_terminal_exception_v1(typeId) {
      throw new Error(`unexpected managed terminal exception type ${typeId}`);
    },
  },
  [`${canonicalPrefix}|wasi:clocks/wall-clock@0.2`]: {
    now(output) {
      var view = new DataView(activeMemory.buffer);
      var base = address(output);
      view.setBigUint64(base, 2n, true);
      view.setUint32(base + 8, 300, true);
    },
  },
});
activeMemory = instance.exports.memory;
if (!(activeMemory instanceof WebAssembly.Memory)) {
  throw new Error("merged module did not export its runtime memory");
}
var value = instance.exports.run(41);
var expectedValue = profile === "clock" ? 44 : 42;
if (value !== expectedValue) {
  throw new Error(`${profile} returned ${value}, expected ${expectedValue}`);
}

console.log(`Component Model ${profile} trimming ${target} PASS`);
