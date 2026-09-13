import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createBrowserTimeZoneMaterializer } from "./browser-timezone-materializer.mjs";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";

const manifestUrl = "https://example.test/deploy%20dir/app.netwasm.deployment.json";
const bytes = Uint8Array.of(8, 9, 10);

test("fetches, verifies, and mounts the segment-encoded browser sidecar", async () => {
  const fixture = createFixture();
  const signal = new AbortController().signal;
  const materialize = createBrowserTimeZoneMaterializer({
    manifestUrl,
    platform: fixture.platform,
  });
  const action = await materialize({ selection: selection(), signal });

  assert.equal(Object.isFrozen(materialize), true);
  assert.deepEqual(fixture.fetches, [{
    url: "https://example.test/deploy%20dir/nested%20dir/app.wasm.tz-info",
    signal,
  }]);
  assert.equal(fixture.mounts[0].guestPath, "/netwasm-timezones/netwasm-timezones.nwtz");
  assert.deepEqual(fixture.mounts[0].bytes, bytes);
  await action.release();
  assert.equal(fixture.releases, 1);
});

test("does not fetch or mount an unselected browser sidecar", async () => {
  const fixture = createFixture();
  const materialize = createBrowserTimeZoneMaterializer({
    manifestUrl,
    platform: fixture.platform,
  });
  assert.equal(await materialize({ selection: null }), null);
  assert.deepEqual(fixture.fetches, []);
  assert.deepEqual(fixture.mounts, []);
});

test("rejects tampered browser bytes before mounting", async () => {
  const fixture = createFixture({ contents: Uint8Array.of(0) });
  const materialize = createBrowserTimeZoneMaterializer({
    manifestUrl,
    platform: fixture.platform,
  });
  await assert.rejects(() => materialize({ selection: selection() }), /integrity/);
  assert.deepEqual(fixture.mounts, []);
});

test("validates browser timezone composition and requests before fetching", () => {
  const valid = createFixture();
  for (const options of [null, 1, [], {},
    { manifestUrl, platform: valid.platform, extra: true },
    Object.create({ manifestUrl, platform: valid.platform })]) {
    assert.throws(() => createBrowserTimeZoneMaterializer(options), /options/);
  }
  const withSymbol = { manifestUrl, platform: valid.platform, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserTimeZoneMaterializer(withSymbol), /options/);
  const withAccessor = { manifestUrl };
  Object.defineProperty(withAccessor, "platform", {
    get: () => valid.platform,
    enumerable: true,
  });
  assert.throws(() => createBrowserTimeZoneMaterializer(withAccessor), /options/);

  for (const platform of [null, 1, [], {}, { ...valid.platform, extra: true },
    Object.create(valid.platform)]) {
    assert.throws(() => createBrowserTimeZoneMaterializer({ manifestUrl, platform }), /platform/);
  }
  const platformWithSymbol = { ...valid.platform, [Symbol("invalid")]: true };
  assert.throws(
    () => createBrowserTimeZoneMaterializer({ manifestUrl, platform: platformWithSymbol }),
    /platform/);
  const platformWithAccessor = { ...valid.platform };
  Object.defineProperty(platformWithAccessor, "fetch", {
    get: () => valid.platform.fetch,
    enumerable: true,
  });
  assert.throws(
    () => createBrowserTimeZoneMaterializer({ manifestUrl, platform: platformWithAccessor }),
    /platform/);
  for (const key of Object.keys(valid.platform)) {
    assert.throws(
      () => createBrowserTimeZoneMaterializer({
        manifestUrl,
        platform: { ...valid.platform, [key]: null },
      }),
      new RegExp(key));
  }
  assert.throws(
    () => createBrowserTimeZoneMaterializer({ manifestUrl: "relative.json", platform: valid.platform }),
    /manifest URL/);

  const materialize = createBrowserTimeZoneMaterializer({ manifestUrl, platform: valid.platform });
  for (const request of [null, 1, [], {}, { selection: null, extra: true },
    Object.create({ selection: null })]) {
    assert.throws(() => materialize(request), /request/);
  }
  const requestWithSymbol = { selection: null, [Symbol("invalid")]: true };
  assert.throws(() => materialize(requestWithSymbol), /request/);
  const requestWithAccessor = {};
  Object.defineProperty(requestWithAccessor, "selection", {
    get: () => null,
    enumerable: true,
  });
  assert.throws(() => materialize(requestWithAccessor), /request/);
  assert.deepEqual(valid.fetches, []);
});

test("keeps browser timezone transport free of Node imports", async () => {
  for (const source of ["browser-timezone-materializer.mjs", "browser-artifact-transport.mjs"]) {
    const contents = await readFile(new URL(source, import.meta.url), "utf8");
    assert.doesNotMatch(contents, /(?:from\s+|import\s*\()["']node:/u);
  }
});

function createFixture({ contents = bytes } = {}) {
  const fetches = [];
  const mounts = [];
  const state = { releases: 0 };
  return {
    fetches,
    mounts,
    get releases() { return state.releases; },
    platform: {
      async fetch(url, options) {
        fetches.push({ url, signal: options.signal });
        return {
          ok: true,
          async arrayBuffer() {
            return contents.buffer.slice(contents.byteOffset, contents.byteOffset + contents.byteLength);
          },
        };
      },
      digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
      mountReadOnlyFile(request) {
        mounts.push(request);
        return () => { state.releases++; };
      },
    },
  };
}

function selection() {
  return selectTimeZoneSidecar({
    runtimeFeatures: ["local-time"],
    artifacts: [
      artifact("nested dir/app.wasm", "application", "application/wasm", "a".repeat(64), null),
      artifact(
        "nested dir/app.wasm.tz-info",
        "timezone-data",
        "application/octet-stream",
        createHash("sha256").update(bytes).digest("hex"),
        1),
    ],
    environment: [{ name: "TZ", value: "Australia/Melbourne" }],
  });
}

function artifact(relativePath, role, mediaType, sha256, schemaVersion) {
  return { relativePath, role, mediaType, sha256, schemaVersion };
}
