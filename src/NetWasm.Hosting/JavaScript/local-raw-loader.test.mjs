import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createLocalRawLoader } from "./local-raw-loader.mjs";

const encoder = new TextEncoder();
const application = Buffer.from(
  "AGFzbQEAAAABBQFgAAF/AhsBD25ldHdhc20uaG9zdC52MQdzZXJ2aWNlAAADAgEABQMBAAEHEAIGbWVtb3J5AgADcnVuAAEKBgEEABAACwAdBG5hbWUBCgEAB3NlcnZpY2UECgEAB3NlcnZpY2U=",
  "base64");
const adapterSource = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({ marker: 7 });
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

test("loads the exact local raw closure with isolated adapter modules", async () => {
  await withDeployment(async fixture => {
    const load = createLocalRawLoader({ manifestPath: fixture.manifestPath });
    assert.equal(Object.isFrozen(load), true);
    const [first, second] = await Promise.all([
      load(fixture.request),
      load({ ...fixture.request, signal: new AbortController().signal }),
    ]);
    assert.equal(first.module instanceof WebAssembly.Module, true);
    assert.equal(first.adapter.rawAdapterMetadata.marker, 7);
    assert.equal(second.adapter.rawAdapterMetadata.marker, 7);
    assert.notEqual(first.adapter, second.adapter);
    assert.deepEqual(first.abi.imports,
      [{ module: "netwasm.host.v1", name: "service", kind: "function" }]);
    assert.deepEqual(first.abi.exports,
      [{ name: "memory", kind: "memory" }, { name: "run", kind: "function" }]);
  });
});

test("rejects tampering before evaluating the raw adapter", async () => {
  await withDeployment(async fixture => {
    const load = createLocalRawLoader({ manifestPath: fixture.manifestPath });
    globalThis.__netwasmRawLocalImportCount = 0;
    await writeFile(fixture.adapterPath, encoder.encode(`
globalThis.__netwasmRawLocalImportCount++;
export const rawAdapterMetadata = {};
export function createAdapter() {}
`));
    await assert.rejects(() => load(fixture.request), /integrity/);
    assert.equal(globalThis.__netwasmRawLocalImportCount, 0);
    delete globalThis.__netwasmRawLocalImportCount;
  });
});

test("rejects dependencies in the verified raw adapter", async () => {
  await withDeployment(async fixture => {
    const source = encoder.encode(`
import "node:assert/strict";
export const rawAdapterMetadata = {};
export function createAdapter() {}
`);
    await writeFile(fixture.adapterPath, source);
    await assert.rejects(() => createLocalRawLoader({ manifestPath: fixture.manifestPath })({
      artifacts: fixture.artifactsWithDigest("raw-adapter", sha256(source)),
    }), /cannot import dependencies/);
  });
});

test("validates the bound local location and exact loader request", () => {
  for (const options of [null, 1, [], {}, { manifestPath: "/tmp/x", extra: true },
    Object.create({ manifestPath: "/tmp/x" })]) {
    assert.throws(() => createLocalRawLoader(options), /options/);
  }
  const withSymbol = { manifestPath: "/tmp/x", [Symbol("invalid")]: true };
  assert.throws(() => createLocalRawLoader(withSymbol), /options/);
  const withAccessor = {};
  Object.defineProperty(withAccessor, "manifestPath", { get: () => "/tmp/x", enumerable: true });
  assert.throws(() => createLocalRawLoader(withAccessor), /options/);
  assert.throws(() => createLocalRawLoader({ manifestPath: "relative.json" }), /canonical and absolute/);

  const load = createLocalRawLoader({ manifestPath: "/tmp/manifest.json" });
  for (const request of [null, 1, [], {}, { artifacts: [], extra: true },
    Object.create({ artifacts: [] })]) {
    assert.throws(() => load(request), /request/);
  }
  const requestWithSymbol = { artifacts: [], [Symbol("invalid")]: true };
  assert.throws(() => load(requestWithSymbol), /request/);
  const requestWithAccessor = {};
  Object.defineProperty(requestWithAccessor, "artifacts", { get: () => [], enumerable: true });
  assert.throws(() => load(requestWithAccessor), /request/);
});

async function withDeployment(action) {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-raw-"));
  try {
    const manifestPath = join(root, "app.netwasm.deployment.json");
    const adapterPath = join(root, "app.raw-adapter.mjs");
    const values = new Map([
      ["application", new Uint8Array(application)],
      ["raw-adapter", adapterSource],
      ["runtime-layout", runtimeLayout],
      ["interop-manifest", interopManifest],
    ]);
    await Promise.all([
      writeFile(join(root, "app.wasm"), values.get("application")),
      writeFile(adapterPath, values.get("raw-adapter")),
      writeFile(join(root, "runtime-layout.json"), values.get("runtime-layout")),
      writeFile(join(root, "interop.json"), values.get("interop-manifest")),
    ]);
    const artifacts = [
      artifact("app.wasm", "application", "application/wasm", values.get("application")),
      artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", values.get("raw-adapter")),
      artifact("runtime-layout.json", "runtime-layout", "application/json", values.get("runtime-layout"), 2),
      artifact("interop.json", "interop-manifest", "application/json", values.get("interop-manifest"), 1),
    ];
    await action({
      manifestPath,
      adapterPath,
      request: { artifacts },
      artifactsWithDigest(role, digest) {
        return artifacts.map(value => value.role === role ? { ...value, sha256: digest } : value);
      },
    });
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

function artifact(relativePath, role, mediaType, bytes, schemaVersion = null) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
