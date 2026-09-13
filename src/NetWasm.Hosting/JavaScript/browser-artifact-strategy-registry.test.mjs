import assert from "node:assert/strict";
import { createHash, webcrypto } from "node:crypto";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { createBrowserArtifactStrategyRegistry } from "./browser-artifact-strategy-registry.mjs";
import { commandExecutionContract } from "./execution-contracts.mjs";

const encoder = new TextEncoder();
const manifestUrl = "https://example.test/deploy/app.netwasm.deployment.json";
const application = Buffer.from(
  "AGFzbQEAAAABDQJgAAF/YAR/f39/AX8CGwEPbmV0d2FzbS5ob3N0LnYxB3NlcnZpY2UAAAMDAgEABQMBAAEHMQQGbWVtb3J5AgANY20zMnAyX21lbW9yeQIADmNtMzJwMl9yZWFsbG9jAAEDcnVuAAIKCwIEAEEICwQAEAALAEkEbmFtZQEYAwAHc2VydmljZQEHcmVhbGxvYwIDcnVuBB0CAAxzZXJ2aWNlLXR5cGUBDHJlYWxsb2MtdHlwZQYJAQAGbWVtb3J5",
  "base64");
const adapterSource = encoder.encode(`
export const contractKey = "wasi-command@0.2.11";
export function createAdapter(generatedModule) {
  return Object.freeze({ contractKey, instantiate: generatedModule.instantiate });
}
`);
const generatedSource = encoder.encode(`
export function instantiate() {
  return { command: { run() { return 31; } } };
}
`);
const coreModule = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);

test("composes exactly location-bound browser execution", async () => {
  const fixture = createPlatform();
  const registry = createBrowserArtifactStrategyRegistry({
    manifestUrl,
    platform: fixture.platform,
  });
  assert.equal(Object.isFrozen(registry), true);
  assert.throws(() => registry.resolve("component"), /unsupported/);
  assert.equal(typeof registry.resolve("raw"), "function");

  const result = await registry.resolve("browser")({
    artifacts: artifacts(),
    contractKey: commandExecutionContract,
    imports: {},
  });
  assert.equal(result.completionKind, "normal");
  assert.equal(result.exitCode, 31);
  assert.deepEqual(fixture.fetches, [
    "https://example.test/deploy/app.wasm.adapter.mjs",
    "https://example.test/deploy/app-component.js",
    "https://example.test/deploy/app-component.core.wasm",
  ]);

});

test("rejects an invalid browser location while composing the registry", () => {
  assert.throws(() => createBrowserArtifactStrategyRegistry({
    manifestUrl: "relative.json",
    platform: createPlatform().platform,
  }), /manifest URL/);
});

test("keeps the browser composition root free of Node imports", async () => {
  const source = await readFile(
    new URL("./browser-artifact-strategy-registry.mjs", import.meta.url),
    "utf8");
  assert.doesNotMatch(source, /(?:from\s+|import\s*\()["']node:/u);
  assert.doesNotMatch(source, /local-(?:component|raw)-loader/u);
});

function createPlatform() {
  const content = new Map([
    ["https://example.test/deploy/app.wasm", application],
    ["https://example.test/deploy/app.wasm.adapter.mjs", adapterSource],
    ["https://example.test/deploy/app-component.js", generatedSource],
    ["https://example.test/deploy/app-component.core.wasm", coreModule],
  ]);
  const moduleSources = new Map();
  const fetches = [];
  let sequence = 0;
  return {
    fetches,
    platform: {
      async fetch(url) {
        fetches.push(url);
        const bytes = content.get(url);
        return {
          ok: bytes !== undefined,
          async arrayBuffer() {
            return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
          },
        };
      },
      digest: webcrypto.subtle.digest.bind(webcrypto.subtle),
      createModuleUrl(bytes) {
        const url = `blob:netwasm-registry/${++sequence}`;
        moduleSources.set(url, new Uint8Array(bytes));
        return url;
      },
      revokeModuleUrl(url) { moduleSources.delete(url); },
      async importModule(url) {
        const source = Buffer.from(moduleSources.get(url)).toString("base64");
        return import(`data:text/javascript;base64,${source}#${encodeURIComponent(url)}`);
      },
      compileCoreModule: WebAssembly.compile.bind(WebAssembly),
    },
  };
}

function artifacts() {
  return [
    artifact("app.wasm", "application", "application/wasm", application),
    artifact("app.wasm.adapter.mjs", "component-adapter", "text/javascript", adapterSource),
    artifact("app-component.js", "component-javascript", "text/javascript", generatedSource),
    artifact("app-component.core.wasm", "component-core-module", "application/wasm", coreModule),
  ];
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
