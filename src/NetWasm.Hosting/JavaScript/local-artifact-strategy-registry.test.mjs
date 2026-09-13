import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { commandExecutionContract } from "./execution-contracts.mjs";
import { createLocalArtifactStrategyRegistry } from "./local-artifact-strategy-registry.mjs";

const encoder = new TextEncoder();
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
  return { command: { run() { return 23; } } };
}
`);
const rawAdapterSource = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({
  abiVersion: 1,
  bindingIdentities: Object.freeze([]),
  requiredCapabilities: Object.freeze([]),
  target: "wasm32",
  witSourceFingerprint: "sha256:${"1".repeat(64)}",
});
export function createAdapter() {
  return Object.freeze({
    imports: Object.freeze(Object.create(null)),
    metadata: rawAdapterMetadata,
  });
}
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
const coreModule = Uint8Array.of(0, 97, 115, 109, 1, 0, 0, 0);

test("composes exactly raw and location-bound component execution locally", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-local-registry-"));
  try {
    const manifestPath = join(root, "app.netwasm.deployment.json");
    await Promise.all([
      writeFile(join(root, "app.wasm"), application),
      writeFile(join(root, "app.wasm.adapter.mjs"), adapterSource),
      writeFile(join(root, "app-component.js"), generatedSource),
      writeFile(join(root, "app-component.core.wasm"), coreModule),
      writeFile(join(root, "app.raw-adapter.mjs"), rawAdapterSource),
      writeFile(join(root, "app.runtime-layout.json"), runtimeLayout),
      writeFile(join(root, "app.interop.json"), interopManifest),
    ]);
    const registry = createLocalArtifactStrategyRegistry({ manifestPath });
    assert.equal(Object.isFrozen(registry), true);
    assert.throws(() => registry.resolve("browser"), /unsupported/);

    const result = await registry.resolve("component")({
      artifacts: artifacts(),
      contractKey: commandExecutionContract,
      imports: {},
    });
    assert.equal(result.completionKind, "normal");
    assert.equal(result.exitCode, 23);

    const calls = [];
    const rawResult = await registry.resolve("raw")({
      artifacts: artifacts(),
      contractKey: commandExecutionContract,
      providers: {},
      prepareInterop({ manifest }) {
        assert.equal(manifest.version, 1);
        calls.push("prepare");
        return {
          imports: { "netwasm.host.v1": { service: () => 43 } },
          bindInstance(instance) {
            assert.equal(instance instanceof WebAssembly.Instance, true);
            calls.push("bind");
          },
          close() { calls.push("close"); },
        };
      },
    });
    assert.equal(rawResult.exitCode, 43);
    assert.deepEqual(calls, ["prepare", "bind", "close"]);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("rejects an invalid local location while composing the registry", () => {
  assert.throws(
    () => createLocalArtifactStrategyRegistry({ manifestPath: "relative.json" }),
    /canonical and absolute/);
});

function artifacts() {
  return [
    artifact("app.wasm", "application", "application/wasm", application),
    artifact("app.wasm.adapter.mjs", "component-adapter", "text/javascript", adapterSource),
    artifact("app-component.js", "component-javascript", "text/javascript", generatedSource),
    artifact("app-component.core.wasm", "component-core-module", "application/wasm", coreModule),
    artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapterSource),
    artifact("app.runtime-layout.json", "runtime-layout", "application/json", runtimeLayout),
    artifact("app.interop.json", "interop-manifest", "application/json", interopManifest),
  ];
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
