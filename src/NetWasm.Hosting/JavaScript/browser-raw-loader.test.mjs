import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";
import { createBrowserRawLoader } from "./browser-raw-loader.mjs";

const encoder = new TextEncoder();
const application = Buffer.from(
  "AGFzbQEAAAABBQFgAAF/AhsBD25ldHdhc20uaG9zdC52MQdzZXJ2aWNlAAADAgEABQMBAAEHEAIGbWVtb3J5AgADcnVuAAEKBgEEABAACwAdBG5hbWUBCgEAB3NlcnZpY2UECgEAB3NlcnZpY2U=",
  "base64");
const adapterSource = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({ marker: 9 });
export function createAdapter() { return Object.freeze({}); }
`);
const runtimeLayout = encoder.encode(JSON.stringify({
  schemaVersion: 2,
  target: "wasm32",
  applicationStaticDataEnd: 64,
  managedExecutableEntryPoint: {
    parameterShape: "none",
    returnShape: "exitCode",
    completionShape: "synchronous",
  },
}));
const interopManifest = encoder.encode(JSON.stringify({ version: 1, target: "wasm32" }));

test("loads segment-encoded raw browser artifacts and revokes the adapter URL", async () => {
  const fixture = createFixture();
  const controller = new AbortController();
  const load = createBrowserRawLoader({
    manifestUrl: fixture.manifestUrl,
    platform: fixture.platform,
  });
  assert.equal(Object.isFrozen(load), true);
  const loaded = await load({ artifacts: fixture.artifacts, signal: controller.signal });
  assert.equal(loaded.module instanceof WebAssembly.Module, true);
  assert.equal(loaded.adapter.rawAdapterMetadata.marker, 9);
  assert.deepEqual(loaded.abi.imports,
    [{ module: "netwasm.host.v1", name: "service", kind: "function" }]);
  assert.deepEqual(fixture.fetches.map(value => value.url), [
    "https://example.test/deploy%20dir/assets/app%20%23%3F%25.wasm",
    "https://example.test/deploy%20dir/assets/app%20%23%3F%25.raw-adapter.mjs",
    "https://example.test/deploy%20dir/assets/runtime-layout.json",
    "https://example.test/deploy%20dir/assets/interop.json",
  ]);
  assert.equal(fixture.fetches.every(value => value.signal === controller.signal), true);
  assert.deepEqual(fixture.revoked, ["blob:netwasm/raw-1"]);
});

test("uses undefined fetch cancellation when the raw caller has no signal", async () => {
  const fixture = createFixture();
  await createBrowserRawLoader({
    manifestUrl: fixture.manifestUrl,
    platform: fixture.platform,
  })({ artifacts: fixture.artifacts });
  assert.equal(fixture.fetches.every(value => value.signal === undefined), true);
});

test("validates browser raw composition and bound requests before fetching", () => {
  const fixture = createFixture();
  for (const options of [null, 1, [], {},
    { manifestUrl: fixture.manifestUrl, platform: fixture.platform, extra: true },
    Object.create({ manifestUrl: fixture.manifestUrl, platform: fixture.platform })]) {
    assert.throws(() => createBrowserRawLoader(options), /options/);
  }
  const withSymbol = {
    manifestUrl: fixture.manifestUrl,
    platform: fixture.platform,
    [Symbol("invalid")]: true,
  };
  assert.throws(() => createBrowserRawLoader(withSymbol), /options/);
  const withAccessor = { manifestUrl: fixture.manifestUrl };
  Object.defineProperty(withAccessor, "platform", { enumerable: true, get: () => fixture.platform });
  assert.throws(() => createBrowserRawLoader(withAccessor), /options/);

  for (const platform of [null, 1, [], {}, { ...fixture.platform, extra: true },
    Object.create(fixture.platform)]) {
    assert.throws(() => createBrowserRawLoader({ manifestUrl: fixture.manifestUrl, platform }), /platform/);
  }
  const platformWithSymbol = { ...fixture.platform, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserRawLoader({
    manifestUrl: fixture.manifestUrl,
    platform: platformWithSymbol,
  }), /platform/);
  const platformWithAccessor = { ...fixture.platform };
  Object.defineProperty(platformWithAccessor, "fetch", { enumerable: true, get: () => async () => ({}) });
  assert.throws(() => createBrowserRawLoader({
    manifestUrl: fixture.manifestUrl,
    platform: platformWithAccessor,
  }), /platform/);
  for (const key of Object.keys(fixture.platform)) {
    assert.throws(() => createBrowserRawLoader({
      manifestUrl: fixture.manifestUrl,
      platform: { ...fixture.platform, [key]: null },
    }), new RegExp(key));
  }
  assert.throws(() => createBrowserRawLoader({
    manifestUrl: "relative.json",
    platform: fixture.platform,
  }), /manifest URL/);

  const load = createBrowserRawLoader({
    manifestUrl: fixture.manifestUrl,
    platform: fixture.platform,
  });
  for (const request of [null, 1, [], {}, { artifacts: [], extra: true },
    Object.create({ artifacts: [] })]) {
    assert.throws(() => load(request), /request/);
  }
  const requestWithSymbol = { artifacts: [], [Symbol("invalid")]: true };
  assert.throws(() => load(requestWithSymbol), /request/);
  const requestWithAccessor = {};
  Object.defineProperty(requestWithAccessor, "artifacts", { enumerable: true, get: () => [] });
  assert.throws(() => load(requestWithAccessor), /request/);
  assert.equal(fixture.fetches.length, 0);
});

function createFixture() {
  const manifestUrl = "https://example.test/deploy%20dir/app.netwasm.deployment.json";
  const values = new Map([
    ["assets/app #?%.wasm", new Uint8Array(application)],
    ["assets/app #?%.raw-adapter.mjs", adapterSource],
    ["assets/runtime-layout.json", runtimeLayout],
    ["assets/interop.json", interopManifest],
  ]);
  const artifacts = [
    artifact("assets/app #?%.wasm", "application", "application/wasm", values.get("assets/app #?%.wasm")),
    artifact("assets/app #?%.raw-adapter.mjs", "raw-adapter", "text/javascript", values.get("assets/app #?%.raw-adapter.mjs")),
    artifact("assets/runtime-layout.json", "runtime-layout", "application/json", values.get("assets/runtime-layout.json"), 2),
    artifact("assets/interop.json", "interop-manifest", "application/json", values.get("assets/interop.json"), 1),
  ];
  const fetches = [];
  const moduleSources = new Map();
  const revoked = [];
  let nextModule = 0;
  const platform = {
    async fetch(url, options) {
      fetches.push({ url, signal: options.signal });
      const relativePath = decodeURIComponent(new URL(url).pathname.split("/").slice(2).join("/"));
      const bytes = values.get(relativePath);
      return {
        ok: bytes !== undefined,
        async arrayBuffer() {
          return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
        },
      };
    },
    digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
    createModuleUrl(bytes, mediaType) {
      assert.equal(mediaType, "text/javascript");
      const url = `blob:netwasm/raw-${++nextModule}`;
      moduleSources.set(url, new Uint8Array(bytes));
      return url;
    },
    revokeModuleUrl(url) {
      revoked.push(url);
      moduleSources.delete(url);
    },
    async importModule(url) {
      const source = Buffer.from(moduleSources.get(url)).toString("base64");
      return import(`data:text/javascript;base64,${source}#${encodeURIComponent(url)}`);
    },
    compileCoreModule: WebAssembly.compile.bind(WebAssembly),
  };
  return { manifestUrl, artifacts, platform, fetches, revoked };
}

function artifact(relativePath, role, mediaType, bytes, schemaVersion = null) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
