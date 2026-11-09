import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { isAbsolute, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const buildFingerprint = "1".repeat(64);
const encoder = new TextEncoder();
const launcherPath = process.env.NETWASM_HOSTING_LAUNCHER_PATH
  ?? fileURLToPath(new URL("./launcher.mjs", import.meta.url));
if (!isAbsolute(launcherPath)) {
  throw new TypeError("NETWASM_HOSTING_LAUNCHER_PATH must be absolute");
}
const application = Buffer.from(
  "AGFzbQEAAAABDQJgBH9/f38Bf2AAAX8DAwIAAQUDAQABBzEEBm1lbW9yeQIADWNtMzJwMl9tZW1vcnkCAA5jbTMycDJfcmVhbGxvYwAAA3J1bgABCgsCBABBCAsEAEElCw==",
  "base64");
const rawAdapter = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({
  abiVersion: 2,
  bindingIdentities: Object.freeze([]),
  requiredCapabilities: Object.freeze([]),
  target: "wasm32",
  witSourceFingerprint: "sha256:${buildFingerprint}",
});
export function createAdapter() {
  return Object.freeze({ imports: Object.freeze(Object.create(null)), metadata: rawAdapterMetadata });
}
`);
const runtimeLayout = encoder.encode(JSON.stringify({
  schemaVersion: 2,
  target: "wasm32",
  applicationStaticDataEnd: 64,
  managedExecutableEntryPoint: {
    parameterShape: "none", returnShape: "exitCode", completionShape: "synchronous",
  },
}));
const interopManifest = encoder.encode(JSON.stringify({
  version: 2,
  target: "wasm32",
  statusAbi: { successStatus: 0, hostFailureStatus: 1, scalarResultOffset: 0 },
  targetLayout: {
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
    spanReferenceOffset: 0,
    spanLengthOffset: 4,
  },
  imports: [], exports: [], callbacks: [],
}));

test("package launcher executes a raw deployment and isolates its result channel", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-launcher-"));
  try {
    const deploymentRoot = join(root, "deployment");
    const { manifestPath, manifestSha256 } = await writeDeployment(deploymentRoot);
    const descriptorPath = join(root, "app.netwasm.execution.json");
    const requestPath = join(root, "request.json");
    const resultPath = join(root, "result.json");
    await Promise.all([
      writeFile(descriptorPath, JSON.stringify({
        schemaVersion: 1,
        buildFingerprint,
        deploymentManifestPath: manifestPath,
        deploymentManifestSha256: manifestSha256,
        hostingVersion: "1",
        hostExecutablePath: process.execPath,
        launcherPath,
      })),
      writeFile(requestPath, JSON.stringify({
        schemaVersion: 1,
        buildFingerprint,
        deploymentManifestSha256: manifestSha256,
        arguments: [],
        environment: [],
        grants: {
          environment: [], preopens: [], network: "denyAll", clocks: [], randomness: false, temporaryDirectory: false, workingDirectory: false,
        },
        applicationImports: [],
      })),
    ]);

    const process_ = spawnSync(process.execPath, [
      launcherPath,
      "--netwasm-execution",
      "--descriptor", descriptorPath,
      "--request", requestPath,
      "--result", resultPath,
      "--replace-result",
    ], { encoding: "utf8" });
    assert.equal(process_.status, 0, process_.stderr);
    assert.equal(process_.stdout, "");
    assert.equal(process_.stderr, "");
    const result = JSON.parse(await readFile(resultPath, "utf8"));
    assert.deepEqual(
      [result.completionKind, result.exitCode],
      ["normal", 37],
      JSON.stringify(result));
    assert.equal(result.primaryFailure, null);
    assert.deepEqual(result.cleanupFailures, []);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

// A published folder's launcher composes the same modules; this one imports them from here.
test("a launcher run without options runs the deployment beside it", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-standalone-"));
  try {
    await writeDeployment(root);
    const standalone = join(root, "netwasm.host.mjs");
    await writeFile(standalone, [
      'import { fileURLToPath } from "node:url";',
      `import { createLocalNetWasmExecution } from ${JSON.stringify(moduleUrl("local-execution-root.mjs"))};`,
      `import { runLocalNetWasmLauncher } from ${JSON.stringify(moduleUrl("local-launcher-bootstrap.mjs"))};`,
      "await runLocalNetWasmLauncher(createLocalNetWasmExecution, fileURLToPath(import.meta.url));",
    ].join("\n"));

    const process_ = spawnSync(process.execPath, [standalone, "--descriptor", "a b"], {
      encoding: "utf8",
    });
    assert.equal(process_.status, 37, process_.stderr);
    assert.equal(process_.stdout, "");
    assert.equal(process_.stderr, "");
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("a launcher that cannot start says why", () => {
  const process_ = spawnSync(process.execPath, [launcherPath, "a"], { encoding: "utf8" });
  assert.equal(process_.status, 1);
  assert.equal(process_.stdout, "");
  assert.match(process_.stderr, /^NetWasm launcher failed: .*deployment\.json/u);
});

async function writeDeployment(deploymentRoot) {
  await mkdir(deploymentRoot, { recursive: true });
  const manifestPath = join(deploymentRoot, "deployment.json");
  const artifacts = [
    artifact("app.wasm", "application", "application/wasm", application),
    artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapter),
    artifact("app.runtime-layout.json", "runtime-layout", "application/json", runtimeLayout),
    artifact("app.interop.json", "interop-manifest", "application/json", interopManifest),
  ];
  const manifestBytes = encoder.encode(JSON.stringify({
    schemaVersion: 1, semanticBuildId: buildFingerprint, deploymentKind: "raw",
    profile: "netwasm0.1", target: "wasm32", featureSet: "default",
    executionContract: "wasi-command@0.3.2",
    versions: {
      compiler: "1", hosting: "1", runtime: "1", runtimeAbi: "1", sdk: "1", toolchain: "1",
    },
    buildFingerprint, runtimeFeatures: [], artifacts,
    requiredImportModules: [], requiredImports: [], exports: [],
  }));
  await Promise.all([
    writeFile(manifestPath, manifestBytes),
    writeFile(join(deploymentRoot, "app.wasm"), application),
    writeFile(join(deploymentRoot, "app.raw-adapter.mjs"), rawAdapter),
    writeFile(join(deploymentRoot, "app.runtime-layout.json"), runtimeLayout),
    writeFile(join(deploymentRoot, "app.interop.json"), interopManifest),
  ]);
  return { manifestPath, manifestSha256: sha256(manifestBytes) };
}

function moduleUrl(name) {
  return new URL(`./${name}`, import.meta.url).href;
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
