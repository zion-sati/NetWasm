import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createBrowserArtifactTransport } from "./browser-artifact-transport.mjs";
import { createLocalArtifactTransport } from "./local-artifact-transport.mjs";

const bytes = Uint8Array.of(1, 2, 3);

test("binds local artifact reads and SHA-256 to the manifest directory", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-transport-"));
  try {
    await mkdir(join(root, "nested dir"));
    await writeFile(join(root, "nested dir", "app.wasm"), bytes);
    const transport = createLocalArtifactTransport({
      manifestPath: join(root, "app.netwasm.deployment.json"),
    });
    assert.equal(Object.isFrozen(transport), true);
    assert.deepEqual([...await transport.readArtifact({
      relativePath: "nested dir/app.wasm",
    }, null)], [...bytes]);
    assert.equal(transport.hashBytes(bytes), createHash("sha256").update(bytes).digest("hex"));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("rejects invalid local transport options and manifest paths", () => {
  const manifestPath = join(tmpdir(), "app.netwasm.deployment.json");
  for (const options of [null, 1, [], {}, { manifestPath, extra: true },
    Object.create({ manifestPath })]) {
    assert.throws(() => createLocalArtifactTransport(options), /options/);
  }
  const withSymbol = { manifestPath, [Symbol("invalid")]: true };
  assert.throws(() => createLocalArtifactTransport(withSymbol), /options/);
  const withAccessor = {};
  Object.defineProperty(withAccessor, "manifestPath", {
    get: () => manifestPath,
    enumerable: true,
  });
  assert.throws(() => createLocalArtifactTransport(withAccessor), /options/);
  for (const value of [null, "", "relative.json", `${tmpdir()}/../app.json`, `${tmpdir()}\0bad`]) {
    assert.throws(() => createLocalArtifactTransport({ manifestPath: value }), /canonical and absolute/);
  }
});

test("binds segment-encoded browser reads and Web Crypto hashing", async () => {
  const calls = [];
  const signal = new AbortController().signal;
  const transport = createBrowserArtifactTransport({
    manifestUrl: "https://example.test/deploy%20dir/app.netwasm.deployment.json",
    async fetch(url, options) {
      calls.push([url, options.signal]);
      return {
        ok: true,
        async arrayBuffer() {
          return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
        },
      };
    },
    digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
  });
  assert.equal(Object.isFrozen(transport), true);
  assert.deepEqual(await transport.readArtifact({ relativePath: "nested dir/app #.wasm" }, signal), bytes);
  assert.deepEqual(calls, [[
    "https://example.test/deploy%20dir/nested%20dir/app%20%23.wasm",
    signal,
  ]]);
  assert.equal(await transport.hashBytes(bytes), createHash("sha256").update(bytes).digest("hex"));
});

test("rejects invalid browser transport composition", () => {
  const valid = browserOptions();
  for (const options of [null, 1, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    assert.throws(() => createBrowserArtifactTransport(options), /options/);
  }
  const withSymbol = { ...valid, [Symbol("invalid")]: true };
  assert.throws(() => createBrowserArtifactTransport(withSymbol), /options/);
  const withAccessor = { manifestUrl: valid.manifestUrl, fetch: valid.fetch };
  Object.defineProperty(withAccessor, "digest", { get: () => valid.digest, enumerable: true });
  assert.throws(() => createBrowserArtifactTransport(withAccessor), /options/);
  for (const key of ["fetch", "digest"]) {
    assert.throws(
      () => createBrowserArtifactTransport({ ...valid, [key]: null }),
      /requires fetch and digest/);
  }
  for (const manifestUrl of [null, "", "relative.json", "file:///tmp/app.json",
    "https://user@example.test/app.json", "https://user:secret@example.test/app.json",
    "https://example.test/app.json#fragment", "https://example.test/deploy/"]) {
    assert.throws(
      () => createBrowserArtifactTransport({ ...valid, manifestUrl }),
      /manifest URL/);
  }
});

function browserOptions() {
  return {
    manifestUrl: "https://example.test/app.netwasm.deployment.json",
    fetch: async () => {},
    digest: async () => {},
  };
}
