import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import test from "node:test";

test("Toolchain owns the pinned platform-neutral jco and Preview 2 staging inputs", async () => {
  const root = new URL("../../../", import.meta.url);
  const [lockBytes, manifestBytes, policyBytes, projectBytes, stagerBytes] = await Promise.all([
    readFile(new URL("package-lock.json", root)),
    readFile(new URL("src/NetWasm.Toolchain/toolchain-manifest.json", root)),
    readFile(new URL("eng/jco-transpile-closure.json", root)),
    readFile(new URL("src/NetWasm.Toolchain/NetWasm.Toolchain.csproj", root)),
    readFile(new URL("eng/stage-jco-closure.py", root)),
  ]);
  const manifest = JSON.parse(manifestBytes);
  const policy = JSON.parse(policyBytes);
  const assets = new Map(manifest.assets.map(asset => [asset.id, asset]));
  const hash = bytes => createHash("sha256").update(bytes).digest("hex");
  assert.equal(manifest.packageId, "NetWasm.Toolchain");
  assert.equal(policy.lockSha256, hash(lockBytes));
  assert.equal(assets.get("jco.package-lock").sha256, hash(lockBytes));
  assert.equal(assets.get("jco.closure-policy").sha256, hash(policyBytes));
  assert.equal(policy.selectedPackagePaths.length, 55);
  assert.equal(policy.runtimeShimClosure.length, 10);
  assert.ok(policy.runtimeShimClosure.every(path =>
    policy.selectedPackagePaths.some(entry => entry.path === path)));

  const project = projectBytes.toString("utf8");
  assert.match(project, /NetWasmToolchainJcoAssetRoot/);
  assert.match(project, /closure-pack-items\.props/);
  assert.match(project, /PackagePath="tools\/jco\/package-lock\.json"/);
  assert.doesNotMatch(project, /NetWasm\.Browser\.Tools/);
  const stager = stagerBytes.toString("utf8");
  assert.match(stager, /ROOT_PACKAGES/);
  assert.match(stager, /NetWasmToolchainJcoAssetRoot/);
  assert.match(stager, /"tools\/jco"/);

  const importProbe = spawnSync(process.execPath, ["--input-type=module", "-e",
    "const fs = await import('@bytecodealliance/preview2-shim/filesystem'); "
    + "if (typeof fs.createFilesystem !== 'function') process.exit(1);",
  ], { cwd: fileURLToPath(root), encoding: "utf8" });
  assert.equal(importProbe.status, 0, "the pinned public host import must resolve independently");
  assert.equal(importProbe.stderr, "");
});
