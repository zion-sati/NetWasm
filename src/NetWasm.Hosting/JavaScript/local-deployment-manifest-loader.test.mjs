import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  createLocalDeploymentManifestLoader,
} from "./local-deployment-manifest-loader.mjs";

test("loads one strict digest-bound local deployment manifest", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-manifest-"));
  try {
    const manifestPath = join(root, "app.netwasm.deployment.json");
    const text = JSON.stringify(manifest());
    await writeFile(manifestPath, text);
    const load = createLocalDeploymentManifestLoader({ manifestPath });
    const result = await load({
      expectedSha256: createHash("sha256").update(text).digest("hex"),
      signal: null,
    });
    assert.equal(result.deploymentKind, "raw");
    assert.equal(Object.isFrozen(result), true);
    await assert.rejects(
      () => load({ expectedSha256: "0".repeat(64), signal: null }),
      /integrity/);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("rejects malformed local loader options and paths", () => {
  const absolute = "/tmp/app.netwasm.deployment.json";
  for (const value of [null, 1, [], {}, { manifestPath: absolute, extra: true },
    Object.create({ manifestPath: absolute })]) {
    assert.throws(() => createLocalDeploymentManifestLoader(value), TypeError);
  }
  for (const manifestPath of [null, "", "relative.json", "/tmp/../tmp/app.json", "/tmp/a\0b"]) {
    assert.throws(() => createLocalDeploymentManifestLoader({ manifestPath }), /path/);
  }
  const symbolic = { manifestPath: absolute, [Symbol("invalid")]: true };
  assert.throws(() => createLocalDeploymentManifestLoader(symbolic), TypeError);
  const accessor = {};
  Object.defineProperty(accessor, "manifestPath", { enumerable: true, get: () => absolute });
  assert.throws(() => createLocalDeploymentManifestLoader(accessor), TypeError);
});

function manifest() {
  const digest = "1".repeat(64);
  return {
    artifacts: [{ mediaType: "application/wasm", relativePath: "app.wasm", role: "application", schemaVersion: null, sha256: digest }],
    buildFingerprint: digest,
    deploymentKind: "raw",
    executionContract: "wasi-command@0.2.11",
    exports: [],
    featureSet: "default",
    profile: "netwasm0.1",
    requiredImportModules: [],
    requiredImports: [],
    runtimeFeatures: [],
    schemaVersion: 1,
    semanticBuildId: digest,
    target: "wasm32",
    versions: { compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1" },
  };
}
