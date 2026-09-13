import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";

import {
  createExecutionPackageVerifier,
} from "./execution-package-verifier.mjs";

const root = "/packages/netwasm.toolchain/0.1.0-preview.23";
const archivePath = `${root}/netwasm.toolchain.0.1.0-preview.23.nupkg`;
const archive = new TextEncoder().encode("immutable-package");
const archiveDigest = createHash("sha256").update(archive).digest("hex");

function package_(overrides = {}) {
  return Object.freeze({
    id: "NetWasm.Toolchain",
    version: "0.1.0-preview.23",
    rootPath: root,
    sha256: archiveDigest,
    ...overrides,
  });
}

function createFixture(overrides = {}) {
  const calls = [];
  const options = {
    hashBytes(bytes) {
      calls.push(["hash", bytes]);
      return createHash("sha256").update(bytes).digest("hex");
    },
    async readFile(path) {
      calls.push(["read", path]);
      assert.equal(path, archivePath);
      return archive;
    },
    async realPath(path) {
      calls.push(["real", path]);
      return path;
    },
    ...overrides,
  };
  return { calls, verify: createExecutionPackageVerifier(options) };
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

test("execution package verifier binds the restored root to its archive digest", async () => {
  const fixture = createFixture();
  assert.equal(Object.isFrozen(fixture.verify), true);
  const descriptor = package_();
  assert.strictEqual(await fixture.verify(descriptor), descriptor);
  assert.deepEqual(fixture.calls, [
    ["real", root],
    ["real", archivePath],
    ["read", archivePath],
    ["hash", archive],
  ]);
});

test("createExecutionPackageVerifier validates exact dependencies", () => {
  const valid = { hashBytes() {}, readFile() {}, realPath() {} };
  const { invalid, malformed } = malformedDataObjects(valid);
  for (const value of invalid) {
    assert.throws(() => createExecutionPackageVerifier(value), /options is invalid/i);
  }
  for (const value of malformed) {
    assert.throws(() => createExecutionPackageVerifier(value), /options shape/i);
  }
  for (const key of Object.keys(valid)) {
    assert.throws(
      () => createExecutionPackageVerifier({ ...valid, [key]: null }),
      new RegExp(`'${key}' action is required`, "i"));
  }
});

test("execution package verifier rejects malformed descriptors", async () => {
  const base = { ...package_() };
  const { invalid, malformed } = malformedDataObjects(base);
  for (const value of invalid) {
    await assert.rejects(() => createFixture().verify(value), /descriptor is invalid/i);
  }
  for (const value of malformed) {
    await assert.rejects(() => createFixture().verify(value), /descriptor shape/i);
  }
  for (const value of [
    { ...base },
    Object.freeze({ ...base, id: "" }),
    Object.freeze({ ...base, id: "bad/id" }),
    Object.freeze({ ...base, version: "" }),
    Object.freeze({ ...base, version: "bad/version" }),
    Object.freeze({ ...base, rootPath: "relative" }),
    Object.freeze({ ...base, sha256: archiveDigest.toUpperCase() }),
  ]) {
    await assert.rejects(() => createFixture().verify(value), /descriptor is incompatible/i);
  }
  await assert.rejects(
    () => createFixture().verify(package_({ rootPath: "/packages/other/../netwasm.toolchain/0.1.0-preview.23" })),
    /root must be canonical/i);
});

test("execution package verifier rejects escaped or invalid physical archive paths", async () => {
  for (const [physicalRoot, physicalArchive] of [
    [null, archivePath],
    ["relative", archivePath],
    [root, null],
    [root, "relative"],
    [root, "/outside/netwasm.toolchain.0.1.0-preview.23.nupkg"],
  ]) {
    const verify = createFixture({
      async realPath(path) {
        return path === root ? physicalRoot : physicalArchive;
      },
    }).verify;
    await assert.rejects(() => verify(package_()), /archive escapes its restored root/i);
  }
});

test("execution package verifier rejects invalid bytes and digest mismatch", async () => {
  await assert.rejects(
    () => createFixture({ async readFile() { return "not bytes"; } }).verify(package_()),
    /reader returned invalid bytes/i);
  await assert.rejects(
    () => createFixture({ hashBytes() { return "0".repeat(64); } }).verify(package_()),
    /failed integrity validation/i);
});

test("execution package verifier propagates path, read, and hash failures", async () => {
  for (const [key, failure] of [
    ["realPath", new Error("real path failed")],
    ["readFile", new Error("read failed")],
    ["hashBytes", new Error("hash failed")],
  ]) {
    await assert.rejects(
      () => createFixture({ async [key]() { throw failure; } }).verify(package_()),
      error => error === failure);
  }
});
