import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import test from "node:test";
import { createBrowserComponentLoader } from "./browser-component-loader.mjs";

const encoder = new TextEncoder();
const adapterSource = encoder.encode(`
export const contractKey = "wasi-command@0.2.11";
export function createAdapter(generatedModule) {
  return Object.freeze({ contractKey, instantiate: generatedModule.instantiate });
}
`);
const generatedSource = encoder.encode(`
export function instantiate() {
  return { command: { run() { return 17; } } };
}
`);
const coreModule = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);

test("loads segment-encoded browser artifacts and revokes verified module URLs", async () => {
  const fixture = createFixture();
  const controller = new AbortController();
  const load = createLoader(fixture);
  assert.equal(Object.isFrozen(load), true);
  const loaded = await load({
    artifacts: fixture.artifacts,
    signal: controller.signal,
  });

  assert.equal(loaded.adapter.contractKey, "wasi-command@0.2.11");
  assert.equal((await loaded.adapter.instantiate()).command.run(), 17);
  assert.equal(loaded.loadCoreModule("app #?%.core.wasm") instanceof WebAssembly.Module, true);
  assert.deepEqual(fixture.fetches.map(value => value.url), [
    "https://example.test/deploy%20dir/assets/app%20%23%3F%25.wasm.adapter.mjs",
    "https://example.test/deploy%20dir/assets/app%20%23%3F%25.js",
    "https://example.test/deploy%20dir/assets/app%20%23%3F%25.core.wasm",
  ]);
  assert.equal(fixture.fetches.every(value => value.signal === controller.signal), true);
  assert.deepEqual(fixture.revoked, ["blob:netwasm/1", "blob:netwasm/2"]);
  assert.equal(fixture.fetches.some(value => value.url.endsWith("app.wasm")), false);
});

test("uses undefined fetch cancellation when the caller has no signal", async () => {
  const fixture = createFixture();
  await createLoader(fixture)({
    artifacts: fixture.artifacts,
  });
  assert.equal(fixture.fetches.every(value => value.signal === undefined), true);
});

test("rejects unsuccessful and malformed fetch responses before module use", async () => {
  const invalidResponses = [null, 1, {}, { ok: true }, { ok: "true", arrayBuffer() {} }];
  for (const response of invalidResponses) {
    const fixture = createFixture({ fetch: async () => response });
    await assert.rejects(() => fixture.load(), /invalid response/);
    assert.deepEqual(fixture.revoked, []);
  }
  const unsuccessful = createFixture({ fetch: async () => ({ ok: false, async arrayBuffer() { return new ArrayBuffer(0); } }) });
  await assert.rejects(() => unsuccessful.load(), /unsuccessful/);
  assert.deepEqual(unsuccessful.revoked, []);

  for (const value of [null, new Uint8Array(1), "bytes"]) {
    const fixture = createFixture({
      fetch: async () => ({ ok: true, async arrayBuffer() { return value; } }),
    });
    await assert.rejects(() => fixture.load(), /response returned invalid bytes/);
  }
});

test("rejects invalid Web Crypto digests before module use", async () => {
  for (const value of [null, new Uint8Array(32), new ArrayBuffer(0), new ArrayBuffer(31), new ArrayBuffer(33)]) {
    const fixture = createFixture({ digest: async () => value });
    await assert.rejects(() => fixture.load(), /SHA-256/);
    assert.deepEqual(fixture.revoked, []);
  }
});

test("revokes module URLs after import failure and rejects invalid URLs", async () => {
  const failed = createFixture({ importModule: async () => { throw new Error("import failed"); } });
  await assert.rejects(() => failed.load(), /import failed/);
  assert.deepEqual(failed.revoked.sort(), ["blob:netwasm/1", "blob:netwasm/2"]);

  for (const value of [null, "", "https://example.test/module.js", "Blob:wrong-case"]) {
    const fixture = createFixture({ createModuleUrl: () => value });
    await assert.rejects(() => fixture.load(), /module URL factory/);
    assert.deepEqual(fixture.revoked, []);
  }
});

test("validates and binds browser platform composition", () => {
  const valid = createFixture();
  for (const options of [null, 1, [], {},
    { manifestUrl: valid.manifestUrl, platform: valid.platform, extra: true },
    Object.create({ manifestUrl: valid.manifestUrl, platform: valid.platform })]) {
    assert.throws(() => createBrowserComponentLoader(options), /options/);
  }
  const optionsWithSymbol = {
    manifestUrl: valid.manifestUrl,
    platform: valid.platform,
    [Symbol("invalid")]: true,
  };
  assert.throws(() => createBrowserComponentLoader(optionsWithSymbol), /options/);
  const optionsWithAccessor = { manifestUrl: valid.manifestUrl };
  Object.defineProperty(optionsWithAccessor, "platform", {
    get: () => valid.platform,
    enumerable: true,
  });
  assert.throws(() => createBrowserComponentLoader(optionsWithAccessor), /options/);

  for (const platform of [null, 1, [], {}, { ...createFixture().platform, extra: true },
    Object.create(createFixture().platform)]) {
    assert.throws(() => createBrowserComponentLoader({
      manifestUrl: valid.manifestUrl,
      platform,
    }), /platform/);
  }
  const withSymbol = { ...createFixture().platform, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserComponentLoader({
    manifestUrl: valid.manifestUrl,
    platform: withSymbol,
  }), /platform/);
  const withAccessor = { ...createFixture().platform };
  Object.defineProperty(withAccessor, "fetch", { get: () => async () => {}, enumerable: true });
  assert.throws(() => createBrowserComponentLoader({
    manifestUrl: valid.manifestUrl,
    platform: withAccessor,
  }), /platform/);
  for (const key of Object.keys(createFixture().platform)) {
    assert.throws(() => createBrowserComponentLoader({
      manifestUrl: valid.manifestUrl,
      platform: { ...createFixture().platform, [key]: null },
    }), new RegExp(key));
  }

  for (const manifestUrl of [null, "", "manifest.json", "https://example.test/a b/manifest.json",
    "file:///tmp/manifest.json", "https://user@example.test/manifest.json",
    "https://user:secret@example.test/manifest.json", "https://example.test/manifest.json#fragment",
    "https://example.test/deploy/"]) {
    assert.throws(() => createBrowserComponentLoader({
      manifestUrl,
      platform: valid.platform,
    }), /manifest URL/);
  }
  assert.equal(valid.fetches.length, 0);
});

test("validates bound browser loader requests before fetching", () => {
  const fixture = createFixture();
  const load = createLoader(fixture);
  for (const request of [null, 1, [], {}, { artifacts: [], extra: true },
    Object.create({ artifacts: [] })]) {
    assert.throws(() => load(request), /request/);
  }
  const withSymbol = { artifacts: [], [Symbol("invalid")]: true };
  assert.throws(() => load(withSymbol), /request/);
  const withAccessor = {};
  Object.defineProperty(withAccessor, "artifacts", { get: () => [], enumerable: true });
  assert.throws(() => load(withAccessor), /request/);
  assert.equal(fixture.fetches.length, 0);
});

function createFixture(overrides = {}) {
  const manifestUrl = "https://example.test/deploy%20dir/app.netwasm.deployment.json";
  const artifacts = [
    artifact("assets/app.wasm", "application", "application/wasm", Uint8Array.of(1)),
    artifact("assets/app #?%.wasm.adapter.mjs", "component-adapter", "text/javascript", adapterSource),
    artifact("assets/app #?%.js", "component-javascript", "text/javascript", generatedSource),
    artifact("assets/app #?%.core.wasm", "component-core-module", "application/wasm", coreModule),
  ];
  const contentByUrl = new Map([
    ["https://example.test/deploy%20dir/assets/app%20%23%3F%25.wasm.adapter.mjs", adapterSource],
    ["https://example.test/deploy%20dir/assets/app%20%23%3F%25.js", generatedSource],
    ["https://example.test/deploy%20dir/assets/app%20%23%3F%25.core.wasm", coreModule],
  ]);
  const fetches = [];
  const moduleSources = new Map();
  const revoked = [];
  let nextModule = 0;
  const defaults = {
    async fetch(url, options) {
      fetches.push({ url, signal: options.signal });
      const bytes = contentByUrl.get(url);
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
      const url = `blob:netwasm/${++nextModule}`;
      moduleSources.set(url, new Uint8Array(bytes));
      return url;
    },
    revokeModuleUrl(url) {
      revoked.push(url);
      moduleSources.delete(url);
    },
    async importModule(url) {
      const bytes = moduleSources.get(url);
      const source = Buffer.from(bytes).toString("base64");
      return import(`data:text/javascript;base64,${source}#${encodeURIComponent(url)}`);
    },
    compileCoreModule: WebAssembly.compile.bind(WebAssembly),
  };
  const platform = { ...defaults, ...overrides };
  return {
    manifestUrl,
    artifacts,
    platform,
    fetches,
    revoked,
    load: () => createBrowserComponentLoader({ manifestUrl, platform })({ artifacts }),
  };
}

function createLoader(fixture) {
  return createBrowserComponentLoader({
    manifestUrl: fixture.manifestUrl,
    platform: fixture.platform,
  });
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
