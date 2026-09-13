import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";

import {
  createBrowserDeploymentManifestLoader,
} from "./browser-deployment-manifest-loader.mjs";

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
  const load = createBrowserDeploymentManifestLoader({ manifestUrl, platform: f.platform });
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
  for (const value of [null, 1, [], {}, { manifestUrl, platform: valid, extra: true },
    Object.create({ manifestUrl, platform: valid })]) {
    assert.throws(() => createBrowserDeploymentManifestLoader(value), TypeError);
  }
  for (const value of [null, "", "relative.json", "ftp://example.test/app.json",
    "https://user@example.test/app.json", "https://example.test/app.json#hash",
    "https://example.test/"]) {
    assert.throws(
      () => createBrowserDeploymentManifestLoader({ manifestUrl: value, platform: valid }),
      /URL/);
  }
  for (const platform of [null, {}, { ...valid, extra: () => {} }, Object.create(valid)]) {
    assert.throws(() => createBrowserDeploymentManifestLoader({ manifestUrl, platform }), /platform/);
  }
  for (const name of Object.keys(valid)) {
    assert.throws(
      () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: { ...valid, [name]: null } }),
      /action/);
  }
  const symbolic = { manifestUrl, platform: valid, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserDeploymentManifestLoader(symbolic), TypeError);
  const accessor = { manifestUrl };
  Object.defineProperty(accessor, "platform", { enumerable: true, get: () => valid });
  assert.throws(() => createBrowserDeploymentManifestLoader(accessor), TypeError);
});

test("rejects malformed and unsuccessful browser responses", async () => {
  const expectedSha256 = "1".repeat(64);
  for (const response of [null, {}, { ok: true }, { ok: "yes", arrayBuffer() {} }]) {
    const f = fixture({ fetch: async () => response });
    const load = createBrowserDeploymentManifestLoader({ manifestUrl, platform: f.platform });
    await assert.rejects(() => load({ expectedSha256, signal: null }), /response/);
  }
  const unsuccessful = fixture({ fetch: async () => ({ ok: false, arrayBuffer() {} }) });
  await assert.rejects(
    () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: unsuccessful.platform })({ expectedSha256, signal: null }),
    /unsuccessful/);
  const invalidBytes = fixture({
    fetch: async () => ({ ok: true, arrayBuffer: async () => new Uint8Array() }),
  });
  await assert.rejects(
    () => createBrowserDeploymentManifestLoader({ manifestUrl, platform: invalidBytes.platform })({ expectedSha256, signal: null }),
    /bytes/);
});

test("rejects malformed Web Crypto digests", async () => {
  for (const digest of [async () => null, async () => new ArrayBuffer(31)]) {
    const f = fixture({ digest });
    const load = createBrowserDeploymentManifestLoader({ manifestUrl, platform: f.platform });
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
