import assert from "node:assert/strict";
import test from "node:test";

import { validateDeploymentManifest } from "./deployment-manifest-validator.mjs";

const digest = character => character.repeat(64);

function artifact(overrides = {}) {
  return {
    relativePath: "app.wasm",
    role: "application",
    mediaType: "application/wasm",
    sha256: digest("a"),
    schemaVersion: null,
    ...overrides,
  };
}

function deploymentFunction(overrides = {}) {
  return {
    interface: "wasi:cli/environment@0.2.11",
    name: "get-arguments",
    parameters: [],
    results: ["list<string>"],
    ...overrides,
  };
}

function manifest(overrides = {}) {
  return {
    schemaVersion: 1,
    semanticBuildId: digest("b"),
    deploymentKind: "component",
    profile: "netwasm0.1",
    target: "wasm32",
    featureSet: "mvp",
    executionContract: "wasi-command@0.2.11",
    versions: {
      sdk: "0.2.0",
      compiler: "0.2.0",
      runtime: "0.2.0",
      runtimeAbi: "1.0.0",
      hosting: "0.2.0",
      toolchain: "0.2.0",
    },
    buildFingerprint: digest("c"),
    runtimeFeatures: [],
    artifacts: [artifact()],
    requiredImportModules: ["wasi:cli/environment@0.2.11"],
    requiredImports: [deploymentFunction()],
    exports: [deploymentFunction({
      interface: "wasi:cli/run@0.2.11",
      name: "run",
      results: ["result"],
    })],
    ...overrides,
  };
}

function expectInvalid(value, pattern) {
  assert.throws(() => validateDeploymentManifest(value), pattern);
}

test("validateDeploymentManifest snapshots every supported kind and target", () => {
  for (const deploymentKind of ["component", "raw", "browser"]) {
    for (const target of ["wasm32", "wasm64"]) {
      const source = manifest({ deploymentKind, target });
      const result = validateDeploymentManifest(source);

      assert.equal(result.deploymentKind, deploymentKind);
      assert.equal(result.target, target);
      assert.equal(Object.isFrozen(result), true);
      assert.equal(Object.isFrozen(result.versions), true);
      assert.equal(Object.isFrozen(result.runtimeFeatures), true);
      assert.equal(Object.isFrozen(result.artifacts), true);
      assert.equal(Object.isFrozen(result.artifacts[0]), true);
      assert.equal(Object.isFrozen(result.requiredImports[0].parameters), true);
      assert.notStrictEqual(result.artifacts, source.artifacts);
      assert.notStrictEqual(result.requiredImports[0], source.requiredImports[0]);
    }
  }

  const nullPrototype = Object.assign(Object.create(null), manifest());
  assert.equal(validateDeploymentManifest(nullPrototype).schemaVersion, 1);
});

test("validateDeploymentManifest accepts the exact optional timezone sidecar", () => {
  const source = manifest({
    runtimeFeatures: ["local-time"],
    artifacts: [
      artifact(),
      artifact({
        relativePath: "app.wasm.tz-info",
        role: "timezone-data",
        mediaType: "application/octet-stream",
        sha256: digest("d"),
        schemaVersion: 1,
      }),
    ],
  });
  const result = validateDeploymentManifest(source);
  assert.deepEqual(result.runtimeFeatures, ["local-time"]);
  assert.equal(result.artifacts[1].role, "timezone-data");
});

test("validateDeploymentManifest preserves type-only required import modules", () => {
  const source = manifest({
    requiredImportModules: [
      "wasi:cli/environment@0.2.11",
      "wasi:io/error@0.2.11",
    ],
  });
  assert.deepEqual(validateDeploymentManifest(source).requiredImportModules, [
    "wasi:cli/environment@0.2.11",
    "wasi:io/error@0.2.11",
  ]);
  expectInvalid(manifest({ requiredImportModules: null }), /must be explicit/i);
  expectInvalid(manifest({
    requiredImportModules: [
      "wasi:cli/environment@0.2.11",
      "wasi:cli/environment@0.2.11",
    ],
  }), /duplicated/i);
});

test("validateDeploymentManifest rejects invalid root objects and core identity", () => {
  const withSymbol = manifest();
  withSymbol[Symbol("hidden")] = true;
  const inherited = Object.assign(Object.create({ inherited: true }), manifest());
  const extra = { ...manifest(), extra: true };
  const wrongKey = manifest();
  delete wrongKey.target;
  wrongKey.otherTarget = "wasm32";
  const nonEnumerable = manifest();
  Object.defineProperty(nonEnumerable, "target", { value: "wasm32", enumerable: false });
  const accessor = manifest();
  Object.defineProperty(accessor, "target", { get: () => "wasm32", enumerable: true });

  for (const value of [null, 1, [], withSymbol]) expectInvalid(value, /manifest is invalid/i);
  for (const value of [inherited, extra, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(value, /manifest shape/i);
  }
  for (const value of [0, 2]) expectInvalid(manifest({ schemaVersion: value }), /schema/i);
  for (const key of ["semanticBuildId", "buildFingerprint"]) {
    for (const value of [null, "", digest("A"), "a".repeat(63), digest("g")]) {
      expectInvalid(manifest({ [key]: value }), /lowercase SHA-256/i);
    }
  }
  for (const deploymentKind of [null, "", "future"]) {
    expectInvalid(manifest({ deploymentKind }), /deployment kind/i);
  }
  for (const profile of [null, "", "net10.0"]) {
    expectInvalid(manifest({ profile }), /managed profile/i);
  }
  for (const target of [null, "", "wasm128"]) {
    expectInvalid(manifest({ target }), /deployment target/i);
  }
  for (const featureSet of [null, "", " padded", "bad\nvalue", "bad\u007fvalue"]) {
    expectInvalid(manifest({ featureSet }), /feature set/i);
  }
  for (const executionContract of [null, "", "WASI@1.0.0", "bad@1.0", "bad@1.x.0"]){
    expectInvalid(manifest({ executionContract }), /execution contract/i);
  }
});

test("validateDeploymentManifest rejects invalid version and runtime-feature data", () => {
  for (const versions of [null, [], { ...manifest().versions, extra: "x" }]) {
    expectInvalid(manifest({ versions }), /deployment versions/i);
  }
  for (const key of Object.keys(manifest().versions)) {
    for (const value of [null, "", " padded", "bad\0value"]) {
      expectInvalid(manifest({ versions: { ...manifest().versions, [key]: value } }), /deployment version/i);
    }
  }
  expectInvalid(manifest({ runtimeFeatures: null }), /runtime features must be explicit/i);
  for (const runtimeFeatures of [["Local-Time"], ["bad_feature"]]) {
    expectInvalid(manifest({ runtimeFeatures }), /canonical lowercase/i);
  }
  expectInvalid(manifest({ runtimeFeatures: ["future"] }), /unsupported/i);
  expectInvalid(manifest({ runtimeFeatures: ["local-time", "local-time"] }), /duplicated/i);
});

test("validateDeploymentManifest rejects malformed artifact objects and values", () => {
  expectInvalid(manifest({ artifacts: null }), /must contain an application/i);
  expectInvalid(manifest({ artifacts: [] }), /must contain an application/i);

  const withSymbol = artifact();
  withSymbol[Symbol("hidden")] = true;
  const inherited = Object.assign(Object.create({ inherited: true }), artifact());
  const extra = { ...artifact(), extra: true };
  const wrongKey = artifact();
  delete wrongKey.role;
  wrongKey.otherRole = "application";
  const nonEnumerable = artifact();
  Object.defineProperty(nonEnumerable, "role", { value: "application", enumerable: false });
  const accessor = artifact();
  Object.defineProperty(accessor, "role", { get: () => "application", enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    expectInvalid(manifest({ artifacts: [value] }), /artifact is invalid/i);
  }
  for (const value of [inherited, extra, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(manifest({ artifacts: [value] }), /artifact shape/i);
  }

  for (const relativePath of [
    null, "", " padded", "bad\0path", "/app.wasm", "C:/app.wasm",
    "nested\\app.wasm", "a//b", "./app.wasm", "a/../b",
  ]) {
    expectInvalid(manifest({ artifacts: [artifact({ relativePath })] }), /artifact path/i);
  }
  for (const role of [null, "", "Application", "bad_role"]) {
    expectInvalid(manifest({ artifacts: [artifact({ role })] }), /artifact role|exactly one application/i);
  }
  for (const mediaType of [null, "", "wasm", " bad/type", "bad\0/type"]) {
    expectInvalid(manifest({ artifacts: [artifact({ mediaType })] }), /media type/i);
  }
  for (const sha256 of [null, "", digest("A"), digest("g")]) {
    expectInvalid(manifest({ artifacts: [artifact({ sha256 })] }), /artifact digest/i);
  }
  for (const schemaVersion of [0, -1, 1.5, "1"]) {
    expectInvalid(manifest({ artifacts: [artifact({ schemaVersion })] }), /schema version/i);
  }
});

test("validateDeploymentManifest enforces artifact uniqueness and timezone ownership", () => {
  const receipt = artifact({
    relativePath: "receipt.json",
    role: "compiler-receipt",
    mediaType: "application/json",
    sha256: digest("e"),
    schemaVersion: 1,
  });
  const timeZone = artifact({
    relativePath: "app.wasm.tz-info",
    role: "timezone-data",
    mediaType: "application/octet-stream",
    sha256: digest("f"),
    schemaVersion: 1,
  });
  expectInvalid(manifest({ artifacts: [artifact(), artifact()] }), /path.*duplicated/i);
  expectInvalid(manifest({ artifacts: [receipt] }), /exactly one application/i);
  expectInvalid(manifest({ artifacts: [artifact(), artifact({ relativePath: "other.wasm" })] }), /exactly one application/i);
  expectInvalid(manifest({ artifacts: [artifact(), timeZone, { ...timeZone, relativePath: "other.tz-info" }] }), /multiple timezone/i);
  expectInvalid(manifest({ artifacts: [artifact(), timeZone] }), /requires.*local-time/i);

  for (const invalidTimeZone of [
    { ...timeZone, relativePath: "other.tz-info" },
    { ...timeZone, mediaType: "application/json" },
    { ...timeZone, schemaVersion: null },
  ]) {
    expectInvalid(manifest({
      runtimeFeatures: ["local-time"],
      artifacts: [artifact(), invalidTimeZone],
    }), /adjacent schema-1/i);
  }
});

test("validateDeploymentManifest validates function inventories and signatures", () => {
  for (const key of ["requiredImports", "exports"]) {
    expectInvalid(manifest({ [key]: null }), new RegExp(`${key === "requiredImports" ? "required imports" : "exports"} must be explicit`, "i"));
  }

  const withSymbol = deploymentFunction();
  withSymbol[Symbol("hidden")] = true;
  const inherited = Object.assign(Object.create({ inherited: true }), deploymentFunction());
  const extra = { ...deploymentFunction(), extra: true };
  const wrongKey = deploymentFunction();
  delete wrongKey.name;
  wrongKey.otherName = "get-arguments";
  const nonEnumerable = deploymentFunction();
  Object.defineProperty(nonEnumerable, "name", { value: "get-arguments", enumerable: false });
  const accessor = deploymentFunction();
  Object.defineProperty(accessor, "name", { get: () => "get-arguments", enumerable: true });
  for (const value of [null, 1, [], withSymbol]) {
    expectInvalid(manifest({ requiredImports: [value] }), /function is invalid/i);
  }
  for (const value of [inherited, extra, wrongKey, nonEnumerable, accessor]) {
    expectInvalid(manifest({ requiredImports: [value] }), /function shape/i);
  }

  for (const interfaceName of [null, "", "WASI@1.0.0", "wasi@1.0"]) {
    expectInvalid(manifest({ requiredImports: [deploymentFunction({ interface: interfaceName })] }), /function interface/i);
  }
  for (const name of [
    null, "", "Get", "bad_name", "[method]descriptor", "[method].read",
    "[method]descriptor.read.more", "[static]Descriptor.open",
    "[constructor]descriptor.open", "[resource-drop]", "[resource-drop]Descriptor",
    "[export-resource-new]descriptor.more",
  ]) {
    expectInvalid(manifest({ requiredImports: [deploymentFunction({ name })] }), /function name/i);
  }

  for (const name of [
    "[constructor]descriptor",
    "[method]descriptor.read-via-stream",
    "[static]descriptor.open-at",
    "[resource-drop]descriptor",
    "[resource-dtor]descriptor",
    "[export-resource-new]descriptor",
    "[export-resource-rep]descriptor",
    "[export-resource-drop]descriptor",
  ]) {
    assert.equal(validateDeploymentManifest(manifest({
      requiredImports: [deploymentFunction({ name })],
    })).requiredImports[0].name, name);
  }
  const func = deploymentFunction();
  expectInvalid(manifest({ requiredImports: [func, { ...func }] }), /function.*duplicated/i);
  for (const parameters of [null, [null], [""], [" padded"], ["bad\0value"]]) {
    expectInvalid(manifest({ requiredImports: [deploymentFunction({ parameters })] }), /signature/i);
  }
  for (const results of [null, [null], [""], [" padded"], ["bad\0value"]]) {
    expectInvalid(manifest({ requiredImports: [deploymentFunction({ results })] }), /signature/i);
  }
});
