import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";

import {
  createBrowserDeploymentManifestLoader,
} from "./browser-deployment-manifest-loader.mjs";
import { validateDeploymentManifest as snapshotManifest } from "./deployment-manifest-validator.mjs";

const manifestUrl = "https://example.test/deploy/app.netwasm.deployment.json";

function fixture(overrides = {}) {
  const text = JSON.stringify(manifest());
  const bytes = new TextEncoder().encode(text);
  const calls = [];
  const platform = {
    async fetch(url, options) {
      calls.push(["fetch", url, options]);
      return {
        ok: true,
        async arrayBuffer() {
          return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
        },
      };
    },
    digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
    ...overrides,
  };
  return { bytes, calls, platform, text };
}

test("loads one strict digest-bound browser deployment manifest", async () => {
  const f = fixture();
  const load = createBrowserDeploymentManifestLoader({ manifestUrl, platform: f.platform, snapshotManifest });
  const result = await load({
    expectedSha256: createHash("sha256").update(f.bytes).digest("hex"),
    signal: null,
  });
  assert.equal(result.deploymentKind, "browser");
  assert.equal(Object.isFrozen(result), true);
  assert.deepEqual(f.calls, [["fetch", manifestUrl, { signal: undefined }]]);
});

test("validates browser options, URL, and platform before fetching", () => {
  const valid = fixture().platform;
  for (const value of [null, 1, [], {}, { manifestUrl, platform: valid, snapshotManifest, extra: true },
    Object.create({ manifestUrl, platform: valid, snapshotManifest })]) {
    assert.throws(() => createBrowserDeploymentManifestLoader(value), TypeError);
  }
  for (const value of [null, "", "relative.json", "ftp://example.test/app.json",
    "https://user@example.test/app.json", "https://example.test/app.json#hash",
    "https://example.test/"]) {
    assert.throws(
      () => createBrowserDeploymentManifestLoader({ manifestUrl: value, platform: valid, snapshotManifest }),
      /URL/);
  }
  for (const platform of [null, {}, { ...valid, extra: () => {} }, Object.create(valid)]) {
    assert.throws(() => createBrowserDeploymentManifestLoader({ manifestUrl, platform, snapshotManifest }), /platform/);
  }
  for (const name of Object.keys(valid)) {
    assert.throws(
      () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: { ...valid, [name]: null }, snapshotManifest }),
      /action/);
  }
  const symbolic = { manifestUrl, platform: valid, snapshotManifest, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserDeploymentManifestLoader(symbolic), TypeError);
  const accessor = { manifestUrl, snapshotManifest };
  Object.defineProperty(accessor, "platform", { enumerable: true, get: () => valid });
  assert.throws(() => createBrowserDeploymentManifestLoader(accessor), TypeError);
});

test("requires a snapshot action and uses it as the manifest's only check", async () => {
  const valid = fixture().platform;
  for (const value of [undefined, null, {}]) {
    assert.throws(
      () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: valid, snapshotManifest: value }),
      /snapshot action/);
  }
  const f = fixture();
  const seen = [];
  const frozen = createBrowserDeploymentManifestLoader({
    manifestUrl,
    platform: f.platform,
    snapshotManifest: value => { seen.push(value); return Object.freeze({ trusted: true }); },
  });
  const expectedSha256 = createHash("sha256").update(f.bytes).digest("hex");
  assert.deepEqual(await frozen({ expectedSha256, signal: null }), { trusted: true });
  assert.equal(seen.length, 1);
  assert.equal(seen[0].schemaVersion, 1);
});

test("rejects malformed and unsuccessful browser responses", async () => {
  const expectedSha256 = "1".repeat(64);
  for (const response of [null, {}, { ok: true }, { ok: "yes", arrayBuffer() {} }]) {
    const f = fixture({ fetch: async () => response });
    const load = createBrowserDeploymentManifestLoader({ manifestUrl, platform: f.platform, snapshotManifest });
    await assert.rejects(() => load({ expectedSha256, signal: null }), /response/);
  }
  const unsuccessful = fixture({ fetch: async () => ({ ok: false, arrayBuffer() {} }) });
  await assert.rejects(
    () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: unsuccessful.platform, snapshotManifest })({ expectedSha256, signal: null }),
    /unsuccessful/);
  const invalidBytes = fixture({
    fetch: async () => ({ ok: true, arrayBuffer: async () => new Uint8Array() }),
  });
  await assert.rejects(
    () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: invalidBytes.platform, snapshotManifest })({ expectedSha256, signal: null }),
    /bytes/);
});

test("rejects malformed Web Crypto digests", async () => {
  for (const digest of [async () => null, async () => new ArrayBuffer(31)]) {
    const f = fixture({ digest });
    const load = createBrowserDeploymentManifestLoader({ manifestUrl, platform: f.platform, snapshotManifest });
    await assert.rejects(
      () => load({ expectedSha256: "1".repeat(64), signal: null }),
      /digest/);
  }
});

function manifest() {
  const digest = "1".repeat(64);
  return {
    artifacts: [{ mediaType: "application/wasm", relativePath: "app.wasm", role: "application", schemaVersion: null, sha256: digest }],
    buildFingerprint: digest,
    deploymentKind: "browser",
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
