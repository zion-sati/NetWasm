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
  const measured = JSON.parse(await readFile(join(runtimeRoot, target, "layout.json"), "utf8"));
  const runtimeArchive = await describeAsset(`${target}/libnetwasm-runtime.a`);
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
    systemLibraries: {
      names: targetPolicy.systemLibraries,
      assets: await Promise.all(targetPolicy.systemLibraries.map((name) =>
        describeAsset(`${target}/system-libraries/${name}`))),
    },
  });
}

const manifest = {
  schemaVersion: 3,
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
