import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import { createPreview2PlatformLoader } from "./preview2-platform-loader.mjs";

const root = "/packages/toolchain";
const shimPrefix = "node_modules/@bytecodealliance/preview2-shim";
const instantiationPath = `${shimPrefix}/dist/common/instantiation.js`;
const filesystemPath = `${shimPrefix}/dist/nodejs/filesystem.js`;
const packagePath = `${shimPrefix}/package.json`;
const encode = value => new TextEncoder().encode(
  typeof value === "string" ? value : JSON.stringify(value));
const hash = bytes => createHash("sha256").update(bytes).digest("hex");
const digest = character => character.repeat(64);

function toolPackage(overrides = {}) {
  return Object.freeze({
    id: "NetWasm.Toolchain",
    version: "0.2.0-preview.23",
    rootPath: root,
    sha256: digest("a"),
    ...overrides,
  });
}

function policy(overrides = {}) {
  return {
    schemaVersion: "1",
    contract: "jco-transpile-only",
    roots: [],
    entryPoint: "unused.mjs",
    requiredCommands: [],
    acceptedInvocation: [],
    evidence: {},
    compilerClosure: [],
    runtimeShimClosure: [shimPrefix],
    selectedPackagePaths: [],
    excludedPackagePaths: [],
    lockSha256: digest("b"),
    ...overrides,
  };
}

function shimPackage(overrides = {}) {
  return {
    name: "@bytecodealliance/preview2-shim",
    version: "0.24.1",
    exports: {
      "./*": { node: { default: "./dist/nodejs/*.js" } },
      "./instantiation": { node: "./dist/common/instantiation.js" },
    },
    ...overrides,
  };
}

function createFixture(overrides = {}) {
  const instantiationBytes = encode("instantiation-module");
  const filesystemBytes = encode("filesystem-module");
  const packageBytes = encode(Object.hasOwn(overrides, "shimPackage")
    ? overrides.shimPackage
    : shimPackage());
  const closure = Object.hasOwn(overrides, "closure") ? overrides.closure : {
    schemaVersion: "1",
    files: [
      { path: packagePath, sha256: hash(packageBytes) },
      { path: instantiationPath, sha256: hash(instantiationBytes) },
      { path: filesystemPath, sha256: hash(filesystemBytes) },
    ],
  };
  const closureBytes = encode(closure);
  const policyBytes = encode(Object.hasOwn(overrides, "policy")
    ? overrides.policy
    : policy());
  const assets = Object.hasOwn(overrides, "assets") ? overrides.assets : [
    {
      id: "jco.closure-integrity",
      version: "1",
      relativePath: "tools/jco/closure-integrity.json",
      sha256: hash(closureBytes),
    },
    {
      id: "jco.closure-policy",
      version: "1",
      relativePath: "tools/jco/closure-policy.json",
      sha256: hash(policyBytes),
    },
    {
      id: "preview2-shim.package",
      version: "0.24.1",
      relativePath: `tools/jco/${packagePath}`,
      sha256: hash(packageBytes),
    },
  ];
  const manifestBytes = encode(Object.hasOwn(overrides, "manifest") ? overrides.manifest : {
    schemaVersion: "1",
    packageId: "NetWasm.Toolchain",
    packageVersion: "0.2.0-preview.23",
    assets,
  });
  const files = new Map([
    [`${root}/tools/toolchain-manifest.json`, manifestBytes],
    [`${root}/tools/jco/closure-integrity.json`, closureBytes],
    [`${root}/tools/jco/closure-policy.json`, policyBytes],
    [`${root}/tools/jco/${packagePath}`, packageBytes],
    [`${root}/tools/jco/${instantiationPath}`, instantiationBytes],
    [`${root}/tools/jco/${filesystemPath}`, filesystemBytes],
  ]);
  for (const [path, value] of overrides.files ?? []) files.set(path, value);
  const calls = [];
  class WASIShim {
    constructor(config) {
      this.config = config;
    }
  }
  const createFilesystem = value => ({ value });
  const options = {
    hashBytes(bytes) {
      calls.push(["hash", bytes]);
      return hash(bytes);
    },
    async importModule(url) {
      calls.push(["import", url]);
      if (url.endsWith("/dist/common/instantiation.js")) return { WASIShim };
      if (url.endsWith("/dist/nodejs/filesystem.js")) return { createFilesystem };
      throw new Error(`unexpected import ${url}`);
    },
    async readFile(path) {
      calls.push(["read", path]);
      const bytes = files.get(path);
      if (bytes === undefined) throw new Error(`missing ${path}`);
      return bytes;
    },
    async realPath(path) {
      calls.push(["real", path]);
      return overrides.realPaths?.get(path) ?? path;
    },
    ...overrides.options,
  };
  return { calls, files, load: createPreview2PlatformLoader(options), WASIShim, createFilesystem };
}

function malformedDataObjects(value) {
  const first = Object.keys(value)[0];
  const withSymbol = { ...value, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), value);
  const extra = { ...value, extra: true };
  const missing = { ...value };
  delete missing[first];
  const nonEnumerable = { ...value };
  Object.defineProperty(nonEnumerable, first, { value: value[first], enumerable: false });
  const accessor = { ...value };
  Object.defineProperty(accessor, first, { get: () => value[first], enumerable: true });
  return { invalid: [null, 1, [], withSymbol], malformed: [inherited, extra, missing, nonEnumerable, accessor] };
}

test("Preview 2 platform loader verifies the selected runtime closure before import", async () => {
  const fixture = createFixture();
  assert.equal(Object.isFrozen(fixture.load), true);
  const platform = await fixture.load(toolPackage());
  assert.equal(Object.isFrozen(platform), true);
  assert.strictEqual(platform.createFilesystem, fixture.createFilesystem);
  const config = Object.freeze({ sandbox: Object.freeze({}) });
  const shim = platform.createShim(config);
  assert.ok(shim instanceof fixture.WASIShim);
  assert.strictEqual(shim.config, config);

  const imports = fixture.calls.filter(([kind]) => kind === "import");
  assert.equal(imports.length, 2);
  const lastHash = fixture.calls.map(([kind]) => kind).lastIndexOf("hash");
  const firstImport = fixture.calls.findIndex(([kind]) => kind === "import");
  assert.ok(lastHash < firstImport);
  assert.equal(
    fixture.calls.filter(([kind]) => kind === "hash").length,
    6);
});

test("createPreview2PlatformLoader validates exact action dependencies", () => {
  const valid = {
    hashBytes() {},
    importModule() {},
    readFile() {},
    realPath() {},
  };
  const { invalid, malformed } = malformedDataObjects(valid);
  for (const value of invalid) {
    assert.throws(() => createPreview2PlatformLoader(value), /options is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createPreview2PlatformLoader(value), /options shape/i);
  }
  for (const key of Object.keys(valid)) {
    assert.throws(
      () => createPreview2PlatformLoader({ ...valid, [key]: null }),
      new RegExp(`'${key}' action is required`, "i"));
  }
});

test("Preview 2 platform loader rejects malformed package descriptors", async () => {
  const base = { ...toolPackage() };
  const { invalid, malformed } = malformedDataObjects(base);
  for (const value of invalid) {
    await assert.rejects(() => createFixture().load(value), /descriptor is invalid/i);
  }
  for (const value of malformed) {
    await assert.rejects(() => createFixture().load(value), /descriptor shape/i);
  }
  for (const value of [
    { ...base },
    Object.freeze({ ...base, id: "Other.Toolchain" }),
    Object.freeze({ ...base, version: "" }),
    Object.freeze({ ...base, rootPath: "relative" }),
    Object.freeze({ ...base, sha256: digest("A") }),
  ]) {
    await assert.rejects(() => createFixture().load(value), /descriptor is incompatible/i);
  }
  await assert.rejects(
    () => createFixture().load(toolPackage({ rootPath: "/packages/../packages/toolchain" })),
    /root must be canonical/i);
});

test("Preview 2 platform loader requires a valid physical package root", async () => {
  await assert.rejects(
    () => createFixture({ options: { async realPath() { return null; } } }).load(toolPackage()),
    /physical root is invalid/i);
  await assert.rejects(
    () => createFixture({ options: { async realPath() { return "relative"; } } }).load(toolPackage()),
    /physical root is invalid/i);
});

test("Preview 2 platform loader validates manifest identity and exact assets", async () => {
  for (const manifest of [
    null,
    { schemaVersion: "1", packageId: "NetWasm.Toolchain", packageVersion: "0.2.0-preview.23", assets: [], extra: true },
    { schemaVersion: "2", packageId: "NetWasm.Toolchain", packageVersion: "0.2.0-preview.23", assets: [{}] },
    { schemaVersion: "1", packageId: "Other", packageVersion: "0.2.0-preview.23", assets: [{}] },
    { schemaVersion: "1", packageId: "NetWasm.Toolchain", packageVersion: "other", assets: [{}] },
    { schemaVersion: "1", packageId: "NetWasm.Toolchain", packageVersion: "0.2.0-preview.23", assets: [] },
  ]) {
    const pattern = manifest === null ? /manifest is invalid/i
      : Object.hasOwn(manifest, "extra") ? /manifest shape/i
        : /manifest identity is incompatible/i;
    await assert.rejects(() => createFixture({ manifest }).load(toolPackage()), pattern);
  }

  const validAsset = createFixture().files;
  assert.ok(validAsset.size > 0);
  const baseAssets = createFixture().files;
  assert.ok(baseAssets.size > 0);
  const asset = {
    id: "jco.closure-integrity",
    version: "1",
    relativePath: "tools/jco/closure-integrity.json",
    sha256: digest("a"),
  };
  for (const value of [null, 1, []]) {
    await assert.rejects(
      () => createFixture({ assets: [value] }).load(toolPackage()),
      /manifest asset is invalid/i);
  }
  const missingAssetKey = { ...asset };
  delete missingAssetKey.id;
  for (const value of [{ ...asset, extra: true }, missingAssetKey]) {
    await assert.rejects(
      () => createFixture({ assets: [value] }).load(toolPackage()),
      /manifest asset shape/i);
  }
  for (const mutation of [
    { id: "" },
    { version: "" },
    { relativePath: null },
    { sha256: digest("A") },
  ]) {
    await assert.rejects(
      () => createFixture({ assets: [{ ...asset, ...mutation }] }).load(toolPackage()),
      /manifest asset is incomplete/i);
  }
  await assert.rejects(
    () => createFixture({ assets: [asset, { ...asset }] }).load(toolPackage()),
    /manifest asset.*duplicated/i);
  await assert.rejects(
    () => createFixture({ assets: [{ ...asset, relativePath: "../outside" }] }).load(toolPackage()),
    /canonical relative slash path/i);
});

test("Preview 2 platform loader requires every declared metadata asset and digest", async () => {
  const fixture = createFixture();
  const manifest = JSON.parse(new TextDecoder().decode(
    fixture.files.get(`${root}/tools/toolchain-manifest.json`)));
  for (const id of ["jco.closure-integrity", "jco.closure-policy", "preview2-shim.package"]) {
    await assert.rejects(
      () => createFixture({ assets: manifest.assets.filter(asset => asset.id !== id) }).load(toolPackage()),
      new RegExp(`does not provide '${id}'`, "i"));
  }
  const corrupt = createFixture();
  corrupt.files.set(`${root}/tools/jco/closure-policy.json`, encode("changed"));
  await assert.rejects(() => corrupt.load(toolPackage()), /failed integrity validation/i);
});

test("Preview 2 platform loader rejects invalid policy contracts and paths", async () => {
  const base = policy();
  for (const value of [null, 1, []]) {
    await assert.rejects(
      () => createFixture({ policy: value }).load(toolPackage()),
      /closure policy is invalid/i);
  }
  const missingPolicyKey = { ...base };
  delete missingPolicyKey.schemaVersion;
  for (const value of [{ ...base, extra: true }, missingPolicyKey]) {
    await assert.rejects(
      () => createFixture({ policy: value }).load(toolPackage()),
      /closure policy shape/i);
  }
  for (const value of [
    policy({ schemaVersion: "2" }),
    policy({ contract: "other" }),
    policy({ runtimeShimClosure: null }),
    policy({ runtimeShimClosure: [] }),
    policy({ runtimeShimClosure: ["node_modules/other"] }),
  ]) {
    await assert.rejects(
      () => createFixture({ policy: value }).load(toolPackage()),
      /runtime-shim policy is incompatible/i);
  }
  for (const prefix of [null, "", "/absolute", "node_modules\\bad", "node_modules/../bad", "other/package"]) {
    const expected = prefix === "other/package" ? /outside node_modules/i : /canonical relative slash path/i;
    await assert.rejects(
      () => createFixture({ policy: policy({ runtimeShimClosure: [shimPrefix, prefix] }) }).load(toolPackage()),
      expected);
  }
  await assert.rejects(
    () => createFixture({
      policy: policy({ runtimeShimClosure: [shimPrefix, shimPrefix] }),
    }).load(toolPackage()),
    /runtime-shim package.*duplicated/i);
});

test("Preview 2 platform loader validates the runtime closure manifest", async () => {
  const baseFile = { path: packagePath, sha256: digest("a") };
  for (const closure of [
    null,
    { schemaVersion: "1", files: [], extra: true },
  ]) {
    await assert.rejects(
      () => createFixture({ closure }).load(toolPackage()),
      closure === null ? /integrity manifest is invalid/i : /integrity manifest shape/i);
  }
  for (const closure of [
    { schemaVersion: "2", files: [baseFile] },
    { schemaVersion: "1", files: [] },
  ]) {
    await assert.rejects(
      () => createFixture({ closure }).load(toolPackage()),
      /integrity manifest is empty/i);
  }
  for (const value of [null, 1, []]) {
    await assert.rejects(
      () => createFixture({ closure: { schemaVersion: "1", files: [value] } }).load(toolPackage()),
      /closure file is invalid/i);
  }
  const missingFileKey = { ...baseFile };
  delete missingFileKey.path;
  for (const value of [{ ...baseFile, extra: true }, missingFileKey]) {
    await assert.rejects(
      () => createFixture({ closure: { schemaVersion: "1", files: [value] } }).load(toolPackage()),
      /closure file shape/i);
  }
  for (const file of [
    { ...baseFile, path: "../outside" },
    { ...baseFile, sha256: digest("A") },
  ]) {
    await assert.rejects(
      () => createFixture({ closure: { schemaVersion: "1", files: [file] } }).load(toolPackage()),
      file.path === "../outside" ? /canonical relative slash path/i : /file digest is invalid/i);
  }
  await assert.rejects(
    () => createFixture({
      closure: { schemaVersion: "1", files: [baseFile, { ...baseFile }] },
    }).load(toolPackage()),
    /closure file.*duplicated/i);
  await assert.rejects(
    () => createFixture({
      policy: policy({ runtimeShimClosure: [shimPrefix, "node_modules/missing"] }),
    }).load(toolPackage()),
    /omits a runtime-shim package/i);
});

test("Preview 2 platform loader requires package exports and closure entries", async () => {
  for (const value of [
    null,
    [],
    shimPackage({ name: "other" }),
    shimPackage({ version: "other" }),
    shimPackage({ exports: null }),
  ]) {
    await assert.rejects(
      () => createFixture({ shimPackage: value }).load(toolPackage()),
      /shim package is incompatible/i);
  }
  for (const exports_ of [
    {},
    { "./*": { node: {} }, "./instantiation": { node: "./dist/common/instantiation.js" } },
    { "./*": { node: { default: "/absolute/*.js" } }, "./instantiation": { node: "./dist/common/instantiation.js" } },
    { "./*": { node: { default: "./dist/nodejs/../*.js" } }, "./instantiation": { node: "./dist/common/instantiation.js" } },
  ]) {
    await assert.rejects(
      () => createFixture({ shimPackage: shimPackage({ exports: exports_ }) }).load(toolPackage()),
      /shim export.*incompatible|canonical relative slash path/i);
  }

  const missingModule = createFixture({
    closure: {
      schemaVersion: "1",
      files: [
        { path: packagePath, sha256: hash(encode(shimPackage())) },
        { path: instantiationPath, sha256: hash(encode("instantiation-module")) },
      ],
    },
  });
  await assert.rejects(() => missingModule.load(toolPackage()), /closure omits Preview 2 module/i);
});

test("Preview 2 platform loader rejects escaped files and invalid reader products", async () => {
  const escapedPath = `${root}/tools/jco/closure-policy.json`;
  const escaped = createFixture({ realPaths: new Map([[escapedPath, "/outside/policy.json"]]) });
  await assert.rejects(() => escaped.load(toolPackage()), /escapes its package root/i);

  const invalidRealPath = createFixture({
    options: {
      async realPath(path) {
        return path === escapedPath ? null : path;
      },
    },
  });
  await assert.rejects(() => invalidRealPath.load(toolPackage()), /escapes its package root/i);

  const invalidBytes = createFixture({
    options: {
      async readFile(path) {
        if (path === `${root}/tools/toolchain-manifest.json`) return "text";
        throw new Error("unexpected");
      },
    },
  });
  await assert.rejects(() => invalidBytes.load(toolPackage()), /reader returned invalid bytes/i);
});

test("Preview 2 platform loader rejects incompatible imported modules", async () => {
  await assert.rejects(
    () => createFixture({
      options: { async importModule() { return null; } },
    }).load(toolPackage()),
    /instantiation module is incompatible/i);
  await assert.rejects(
    () => createFixture({
      options: {
        async importModule(url) {
          return url.endsWith("/instantiation.js") ? { WASIShim: class {} } : {};
        },
      },
    }).load(toolPackage()),
    /filesystem module is incompatible/i);
});

test("Preview 2 platform loader propagates I/O, hashing, and import failures", async () => {
  for (const [key, failure] of [
    ["realPath", new Error("real path failed")],
    ["readFile", new Error("read failed")],
    ["hashBytes", new Error("hash failed")],
    ["importModule", new Error("import failed")],
  ]) {
    const options = {
      async [key]() { throw failure; },
    };
    await assert.rejects(
      () => createFixture({ options }).load(toolPackage()),
      error => error === failure);
  }
});
