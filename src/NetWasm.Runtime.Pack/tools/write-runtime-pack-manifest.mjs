import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const [packageRoot, toolchainArgument] = process.argv.slice(2);
if (!packageRoot) {
  throw new Error("usage: write-runtime-pack-manifest.mjs <package-root> [toolchain-path]");
}

const toolchainPath = toolchainArgument ?? join(packageRoot, "..", "..", "eng", "toolchain.json");
const runtimeRoot = join(packageRoot, "runtime");
const policy = JSON.parse(await readFile(join(runtimeRoot, "runtime-policy.json"), "utf8"));
const toolchain = JSON.parse(await readFile(toolchainPath, "utf8"));
const exports = (await readFile(join(runtimeRoot, "runtime-exports.txt"), "utf8"))
  .split(/\r?\n/u)
  .filter(Boolean);

const fingerprintInput = {
  dotnetSdk: toolchain.dotnetSdk,
  emscripten: toolchain.emscripten,
  llvmLld: toolchain.llvmLld,
  wasmTools: toolchain.wasmTools,
  runtimeAbi: toolchain.runtimeAbi,
  bdwgc: toolchain.bdwgc,
};
const toolchainFingerprint = createHash("sha256")
  .update(JSON.stringify(fingerprintInput))
  .digest("hex");

async function describeAsset(relativePath) {
  const bytes = await readFile(join(runtimeRoot, relativePath));
  return {
    path: relativePath,
    sha256: createHash("sha256").update(bytes).digest("hex"),
  };
}

const targets = [];
for (const [target, targetPolicy] of Object.entries(policy.targets)) {
  const pointerType = targetPolicy.pointerSizeBytes === 8 ? 0x7e : 0x7f;
  const prefix = targetPolicy.pointerSizeBytes === 8 ? 'cm64p2' : 'cm32p2';
  const valueTypes = { i32: 0x7f, i64: 0x7e, f32: 0x7d, f64: 0x7c, ptr: pointerType };
  const physicalTypes = values => values.map(value => {
    if (!Object.hasOwn(valueTypes, value)) throw new Error('Unsupported native policy value type');
    return valueTypes[value];
  });
  const measured = JSON.parse(await readFile(join(runtimeRoot, target, "layout.json"), "utf8"));
  const runtimeArchive = await describeAsset(`${target}/libnetwasm-runtime.a`);
  const collectorArchive = await describeAsset(`${target}/libgc.a`);
  const allowedUndefinedSymbols = await describeAsset(
    `${target}/allowed-undefined-symbols.txt`);
  targets.push({
    target,
    pointerSizeBytes: targetPolicy.pointerSizeBytes,
    alignment: policy.alignment,
    runtimeFootprintBytes: measured.runtimeFootprintBytes,
    nativeStackSizeBytes: policy.nativeStackSizeBytes,
    defaultInitialHeapSizeBytes: policy.defaultInitialHeapSizeBytes,
    defaultMaximumMemorySizeBytes: targetPolicy.defaultMaximumMemorySizeBytes,
    maximumMemorySizeBytes: targetPolicy.maximumMemorySizeBytes,
    runtimeArchive,
    collectorArchive,
    allowedUndefinedSymbols,
    nativeValidation: {
      version: policy.staticNative.version,
      features: [...policy.staticNative.features, ...(target === 'wasm64' ? ['memory64'] : [])],
      imports: policy.staticNative.imports.map(imported => ({
        module: imported.module.replace('{prefix}', prefix), name: imported.name,
        parameters: physicalTypes(imported.parameters), results: physicalTypes(imported.results),
        required: imported.required,
      })),
    },
    systemLibraries: {
      names: targetPolicy.systemLibraries,
      assets: await Promise.all(targetPolicy.systemLibraries.map((name) =>
        describeAsset(`${target}/system-libraries/${name}`))),
    },
  });
}

const manifest = {
  schemaVersion: 4,
  runtimeAbi: toolchain.runtimeAbi,
  emscriptenVersion: toolchain.emscripten,
  wasmPageSize: policy.wasmPageSize,
  exports,
  provenance: {
    buildSeam: "eng/build-netwasm-runtime.sh",
    configuration: "release",
    toolchainFile: "eng/toolchain.json",
    toolchainFingerprint,
    runtimeSource: "src/NetWasm.Runtime native runtime and collector sources",
    layoutAuthority: "per-application wasm-ld final link",
  },
  targets,
};

await writeFile(
  join(runtimeRoot, "runtime-pack.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
  "utf8");
