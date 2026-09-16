import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, isAbsolute, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const buildFingerprint = "1".repeat(64);
const encoder = new TextEncoder();
const launcherPath = process.env.NETWASM_HOSTING_LAUNCHER_PATH
  ?? fileURLToPath(new URL("./launcher.mjs", import.meta.url));
if (!isAbsolute(launcherPath)) {
  throw new TypeError("NETWASM_HOSTING_LAUNCHER_PATH must be absolute");
}
const toolchainPackageRoot = process.env.NETWASM_HOSTING_TOOLCHAIN_PACKAGE_ROOT;
if (toolchainPackageRoot !== undefined && !isAbsolute(toolchainPackageRoot)) {
  throw new TypeError("NETWASM_HOSTING_TOOLCHAIN_PACKAGE_ROOT must be absolute");
}
const application = Buffer.from(
  "AGFzbQEAAAABDQJgBH9/f38Bf2AAAX8DAwIAAQUDAQABBzEEBm1lbW9yeQIADWNtMzJwMl9tZW1vcnkCAA5jbTMycDJfcmVhbGxvYwAAA3J1bgABCgsCBABBCAsEAEElCw==",
  "base64");
const rawAdapter = encoder.encode(`
export const rawAdapterMetadata = Object.freeze({
  abiVersion: 1,
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
  version: 1,
  target: "wasm32",
  statusAbi: { successStatus: 0, hostFailureStatus: 1, scalarResultOffset: 0 },
  targetLayout: {
    managedReferenceSize: 4,
    stringLengthOffset: 4,
    stringDataOffset: 8,
    arrayLengthOffset: 4,
    arrayDataPointerOffset: 8,
  },
  imports: [], exports: [], callbacks: [],
}));

test("package launcher executes a raw deployment and isolates its result channel", async () => {
  const root = await mkdtemp(join(tmpdir(), "netwasm-launcher-"));
  try {
    const deploymentRoot = join(root, "deployment");
    const packageRoot = join(root, "packages", "netwasm.toolchain", "0.1.0-preview.23");
    await Promise.all([mkdir(deploymentRoot, { recursive: true }), mkdir(packageRoot, { recursive: true })]);
    const package_ = toolchainPackageRoot === undefined
      ? await writeToolchainPackage(packageRoot)
      : await describeToolchainPackage(toolchainPackageRoot);
    const manifestPath = join(deploymentRoot, "app.netwasm.deployment.json");
    const artifacts = [
      artifact("app.wasm", "application", "application/wasm", application),
      artifact("app.raw-adapter.mjs", "raw-adapter", "text/javascript", rawAdapter),
      artifact("app.runtime-layout.json", "runtime-layout", "application/json", runtimeLayout),
      artifact("app.interop.json", "interop-manifest", "application/json", interopManifest),
    ];
    const manifestBytes = encoder.encode(JSON.stringify({
      schemaVersion: 1, semanticBuildId: buildFingerprint, deploymentKind: "raw",
      profile: "netwasm0.1", target: "wasm32", featureSet: "default",
      executionContract: "wasi-command@0.2.11",
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
    const descriptorPath = join(root, "app.netwasm.execution.json");
    const requestPath = join(root, "request.json");
    const resultPath = join(root, "result.json");
    const manifestSha256 = sha256(manifestBytes);
    await Promise.all([
      writeFile(descriptorPath, JSON.stringify({
        schemaVersion: 1,
        buildFingerprint,
        deploymentManifestPath: manifestPath,
        deploymentManifestSha256: manifestSha256,
        hostingVersion: "1",
        hostExecutablePath: process.execPath,
        launcherPath,
        toolPackages: [package_],
      })),
      writeFile(requestPath, JSON.stringify({
        schemaVersion: 1,
        buildFingerprint,
        deploymentManifestSha256: manifestSha256,
        arguments: [],
        environment: [],
        grants: {
          environment: [], preopens: [], network: "denyAll", clocks: [], randomness: false,
        },
        applicationImports: [],
      })),
    ]);

    const process_ = spawnSync(process.execPath, [
      launcherPath,
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

async function writeToolchainPackage(packageRoot) {
  const shimPrefix = "node_modules/@bytecodealliance/preview2-shim";
  const packagePath = `${shimPrefix}/package.json`;
  const instantiationPath = `${shimPrefix}/dist/common/instantiation.js`;
  const filesystemPath = `${shimPrefix}/dist/nodejs/filesystem.js`;
  const packageBytes = encoder.encode(JSON.stringify({
    name: "@bytecodealliance/preview2-shim",
    version: "0.24.1",
    exports: {
      "./*": { node: { default: "./dist/nodejs/*.js" } },
      "./instantiation": { node: "./dist/common/instantiation.js" },
    },
  }));
  const instantiationBytes = encoder.encode("export class WASIShim { constructor(config) { this.config = config; } getImportObject() { return {}; } }");
  const filesystemBytes = encoder.encode(`
class Descriptor { openAt() { return new Descriptor(); } read() { return [new Uint8Array(), true]; } }
export function createFilesystem() {
  return { types: { Descriptor }, preopens: { getDirectories: () => [] } };
}
`);
  const closureBytes = encoder.encode(JSON.stringify({
    schemaVersion: "1",
    files: [
      { path: packagePath, sha256: sha256(packageBytes) },
      { path: instantiationPath, sha256: sha256(instantiationBytes) },
      { path: filesystemPath, sha256: sha256(filesystemBytes) },
    ],
  }));
  const policyBytes = encoder.encode(JSON.stringify({
    schemaVersion: "1",
    contract: "jco-transpile-only",
    roots: [], entryPoint: "unused", requiredCommands: [], acceptedInvocation: [], evidence: {},
    compilerClosure: [], runtimeShimClosure: [shimPrefix], selectedPackagePaths: [],
    excludedPackagePaths: [], lockSha256: "2".repeat(64),
  }));
  const manifestBytes = encoder.encode(JSON.stringify({
    schemaVersion: "1",
    packageId: "NetWasm.Toolchain",
    packageVersion: "0.1.0-preview.23",
    assets: [
      assetDescriptor("jco.closure-integrity", "1", "tools/jco/closure-integrity.json", closureBytes),
      assetDescriptor("jco.closure-policy", "1", "tools/jco/closure-policy.json", policyBytes),
      assetDescriptor("preview2-shim.package", "0.24.1", `tools/jco/${packagePath}`, packageBytes),
    ],
  }));
  const archiveBytes = encoder.encode("immutable NetWasm.Toolchain archive fixture");
  const files = new Map([
    ["tools/toolchain-manifest.json", manifestBytes],
    ["tools/jco/closure-integrity.json", closureBytes],
    ["tools/jco/closure-policy.json", policyBytes],
    [`tools/jco/${packagePath}`, packageBytes],
    [`tools/jco/${instantiationPath}`, instantiationBytes],
    [`tools/jco/${filesystemPath}`, filesystemBytes],
    ["netwasm.toolchain.0.1.0-preview.23.nupkg", archiveBytes],
  ]);
  await Promise.all([...files].map(async ([path, bytes]) => {
    const absolutePath = join(packageRoot, ...path.split("/"));
    await mkdir(dirname(absolutePath), { recursive: true });
    await writeFile(absolutePath, bytes);
  }));
  return {
    id: "NetWasm.Toolchain",
    version: "0.1.0-preview.23",
    rootPath: packageRoot,
    sha256: sha256(archiveBytes),
  };
}

async function describeToolchainPackage(packageRoot) {
  const version = "0.1.0-preview.23";
  const archiveBytes = await readFile(join(
    packageRoot,
    `netwasm.toolchain.${version}.nupkg`));
  return {
    id: "NetWasm.Toolchain",
    version,
    rootPath: packageRoot,
    sha256: sha256(archiveBytes),
  };
}

function artifact(relativePath, role, mediaType, bytes) {
  return { relativePath, role, mediaType, sha256: sha256(bytes), schemaVersion: null };
}

function assetDescriptor(id, version, relativePath, bytes) {
  return { id, version, relativePath, sha256: sha256(bytes) };
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}
