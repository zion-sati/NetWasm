import assert from "node:assert/strict";
import test from "node:test";

import {
  createExecutionDescriptorValidator,
} from "./execution-descriptor-validator.mjs";

const digest = character => character.repeat(64);

function descriptor(overrides = {}) {
  return {
    schemaVersion: 1,
    buildFingerprint: digest("a"),
    deploymentManifestPath: "/output/app.netwasm.deployment.json",
    deploymentManifestSha256: digest("b"),
    hostingVersion: "0.1.0-preview.1",
    hostExecutablePath: "/tools/node",
    launcherPath: "/packages/hosting/tools/netwasm/hosting/launcher.mjs",
    toolPackages: [{
      id: "NetWasm.Toolchain",
      version: "0.1.0-preview.23",
      rootPath: "/packages/toolchain",
      sha256: digest("c"),
    }],
    ...overrides,
  };
}

function createValidator(predicate = value => value.startsWith("/")) {
  return createExecutionDescriptorValidator({ isAbsoluteHostPath: predicate });
}

function malformedDataObjects(value) {
  const withSymbol = { ...value, [Symbol("hidden")]: true };
  const inherited = Object.assign(Object.create({ inherited: true }), value);
  const extra = { ...value, extra: true };
  const missing = { ...value };
  delete missing[Object.keys(value)[0]];
  const nonEnumerable = { ...value };
  Object.defineProperty(nonEnumerable, Object.keys(value)[0], {
    value: value[Object.keys(value)[0]],
    enumerable: false,
  });
  const accessor = { ...value };
  Object.defineProperty(accessor, Object.keys(value)[0], {
    get: () => value[Object.keys(value)[0]],
    enumerable: true,
  });
  return { invalid: [null, 1, [], withSymbol], malformed: [inherited, extra, missing, nonEnumerable, accessor] };
}

test("execution descriptor validator snapshots the complete descriptor", () => {
  const source = descriptor({
    toolPackages: [
      descriptor().toolPackages[0],
      {
        id: "NetWasm.Hosting",
        version: "0.1.0-preview.1",
        rootPath: "/packages/hosting",
        sha256: digest("d"),
      },
    ],
  });
  const result = createValidator()(source);
  assert.deepEqual(result, source);
  assert.equal(Object.isFrozen(result), true);
  assert.equal(Object.isFrozen(result.toolPackages), true);
  assert.equal(Object.isFrozen(result.toolPackages[0]), true);
  assert.notStrictEqual(result, source);
  assert.notStrictEqual(result.toolPackages, source.toolPackages);
  assert.notStrictEqual(result.toolPackages[0], source.toolPackages[0]);

  const nullPrototype = Object.assign(Object.create(null), descriptor());
  assert.equal(createValidator()(nullPrototype).schemaVersion, 1);
});

test("createExecutionDescriptorValidator validates its exact dependency", () => {
  const valid = { isAbsoluteHostPath() {} };
  const { invalid, malformed } = malformedDataObjects(valid);
  for (const value of invalid) {
    assert.throws(() => createExecutionDescriptorValidator(value), /options is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createExecutionDescriptorValidator(value), /options shape/i);
  }
  assert.throws(
    () => createExecutionDescriptorValidator({ isAbsoluteHostPath: null }),
    /predicate is required/i);
  assert.equal(Object.isFrozen(createValidator()), true);
});

test("execution descriptor validator rejects malformed roots and identities", () => {
  const { invalid, malformed } = malformedDataObjects(descriptor());
  for (const value of invalid) {
    assert.throws(() => createValidator()(value), /descriptor is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createValidator()(value), /descriptor shape/i);
  }
  for (const schemaVersion of [0, 2, "1"]) {
    assert.throws(
      () => createValidator()(descriptor({ schemaVersion })),
      /schema is unsupported/i);
  }
  for (const key of ["buildFingerprint", "deploymentManifestSha256"]) {
    for (const value of [null, "", digest("A"), "a".repeat(63), digest("g")]) {
      assert.throws(
        () => createValidator()(descriptor({ [key]: value })),
        /lowercase SHA-256/i);
    }
  }
  for (const hostingVersion of [null, "", " \t"] ) {
    assert.throws(
      () => createValidator()(descriptor({ hostingVersion })),
      /Hosting version is required/i);
  }
});

test("execution descriptor validator enforces absolute local paths", () => {
  const keys = ["deploymentManifestPath", "hostExecutablePath", "launcherPath"];
  for (const key of keys) {
    for (const value of [null, "", " ", "relative/path", "/bad\0path"]) {
      assert.throws(
        () => createValidator()(descriptor({ [key]: value })),
        new RegExp(`${key === "deploymentManifestPath" ? "deployment manifest" : key === "hostExecutablePath" ? "host executable" : "launcher"} path`, "i"));
    }
  }

  const calls = [];
  const validate = createValidator(path => {
    calls.push(path);
    return path.startsWith("/");
  });
  validate(descriptor());
  assert.deepEqual(calls, [
    "/output/app.netwasm.deployment.json",
    "/tools/node",
    "/packages/hosting/tools/netwasm/hosting/launcher.mjs",
    "/packages/toolchain",
  ]);
});

test("execution descriptor validator enforces package structure and identity", () => {
  for (const toolPackages of [null, [], {}]) {
    assert.throws(
      () => createValidator()(descriptor({ toolPackages })),
      /non-empty array/i);
  }
  const package_ = descriptor().toolPackages[0];
  const { invalid, malformed } = malformedDataObjects(package_);
  for (const value of invalid) {
    assert.throws(
      () => createValidator()(descriptor({ toolPackages: [value] })),
      /tool package is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(
      () => createValidator()(descriptor({ toolPackages: [value] })),
      /tool package shape/i);
  }
  for (const key of ["id", "version"]) {
    for (const value of [null, "", " \n"]) {
      assert.throws(
        () => createValidator()(descriptor({
          toolPackages: [{ ...package_, [key]: value }],
        })),
        /is required/i);
    }
  }
  for (const rootPath of [null, "", "relative", "/bad\0path"]) {
    assert.throws(
      () => createValidator()(descriptor({
        toolPackages: [{ ...package_, rootPath }],
      })),
      /package root/i);
  }
  for (const sha256 of [null, "", digest("D"), "d".repeat(63)]) {
    assert.throws(
      () => createValidator()(descriptor({
        toolPackages: [{ ...package_, sha256 }],
      })),
      /package digest.*lowercase SHA-256/i);
  }
  assert.throws(
    () => createValidator()(descriptor({
      toolPackages: [package_, { ...package_, id: "NETWASM.TOOLCHAIN" }],
    })),
    /package 'NETWASM\.TOOLCHAIN' is duplicated/i);
});
