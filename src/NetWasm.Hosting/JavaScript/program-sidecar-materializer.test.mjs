import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { selectProgramSidecars } from "./program-sidecar-selector.mjs";
import { materializeProgramSidecars } from "./program-sidecar-materializer.mjs";
import { timeZoneContainer } from "./timezone-sidecar-zones.test.mjs";

const sourceBytes = timeZoneContainer([["Australia/Melbourne", 1]]);
const digest = bytes => createHash("sha256").update(bytes).digest("hex");
const application = () => ({
  relativePath: "publish/app.wasm",
  role: "application",
  mediaType: "application/wasm",
  sha256: "a".repeat(64),
  schemaVersion: null,
});
const sidecar = (overrides = {}) => ({
  relativePath: "publish/app.wasm.tz-info",
  role: "timezone-data",
  mediaType: "application/octet-stream",
  sha256: digest(sourceBytes),
  schemaVersion: 1,
  ...overrides,
});
const symbols = (role, overrides = {}) => ({
  relativePath: `publish/app.wasm.${role}`,
  role,
  mediaType: `application/vnd.netwasm.${role};version=1`,
  sha256: "c".repeat(64),
  schemaVersion: 1,
  ...overrides,
});
const selection = () => selectProgramSidecars({
  runtimeFeatures: ["local-time"],
  artifacts: [application(), sidecar()],
  environment: [{ name: "TZ", value: "Australia/Melbourne" }],
});

test("verifies copied bytes and returns one immutable fixed-path cleanup action", async () => {
  const calls = [];
  const transport = new Uint8Array(sourceBytes);
  let hashedBytes;
  let mountedBytes;
  const selected = selection();
  const action = await materializeProgramSidecars({
    selection: selected,
    async readArtifact(artifact, signal) {
      calls.push(["read", artifact, signal]);
      return transport;
    },
    hashBytes(bytes) {
      calls.push(["hash"]);
      hashedBytes = bytes;
      const value = digest(bytes);
      bytes.fill(0);
      return value;
    },
    mountReadOnlyFile(request) {
      calls.push(["mount", request.guestPath]);
      assert.equal(request.guestPath, "/netwasm-sidecars/app.wasm.tz-info");
      assert.equal(Object.isFrozen(request), true);
      mountedBytes = request.bytes;
      return () => calls.push(["release"]);
    },
  });

  assert.equal(Object.isFrozen(action), true);
  assert.equal(action.code, "host.sidecar-mount");
  assert.match(action.message, /release the program's sidecar mounts/);
  assert.deepEqual(calls[0][1], selected.artifacts[0]);
  assert.notEqual(calls[0][1], selected.artifacts[0]);
  assert.equal(calls[0][2], null);
  assert.deepEqual(calls.map(value => value[0]), ["read", "hash", "mount"]);
  assert.deepEqual(mountedBytes, sourceBytes);
  assert.notEqual(mountedBytes, sourceBytes);
  assert.notEqual(hashedBytes, sourceBytes);
  assert.notEqual(hashedBytes, mountedBytes);
  await action.release();
  assert.deepEqual(calls.map(value => value[0]), ["read", "hash", "mount", "release"]);

  transport.fill(9);
  assert.deepEqual(mountedBytes, timeZoneContainer([["Australia/Melbourne", 1]]));
});

test("returns null without invoking transport, hashing, or mounting", async () => {
  const unexpected = () => { throw new Error("must not execute"); };
  assert.equal(await materializeProgramSidecars({
    selection: null,
    readArtifact: unexpected,
    hashBytes: unexpected,
    mountReadOnlyFile: unexpected,
  }), null);
});

test("rejects invalid materialization requests and collaborators before I/O", async () => {
  const valid = request();
  for (const value of [null, 1, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    await assert.rejects(() => materializeProgramSidecars(value), /request/);
  }
  const withSymbol = { ...valid, [Symbol("invalid")]: true };
  await assert.rejects(() => materializeProgramSidecars(withSymbol), /request/);
  const withAccessor = { ...valid };
  Object.defineProperty(withAccessor, "selection", { get: selection, enumerable: true });
  await assert.rejects(() => materializeProgramSidecars(withAccessor), /request/);

  for (const key of ["readArtifact", "hashBytes", "mountReadOnlyFile"]) {
    await assert.rejects(
      () => materializeProgramSidecars({ ...valid, [key]: null }),
      /required/);
  }
  await assert.rejects(
    () => materializeProgramSidecars({ ...valid, signal: {} }),
    /AbortSignal/);
});

test("requires one exact non-UTC timezone selection", async () => {
  const valid = request();
  for (const value of [1, [], {}, { timeZone: "Australia/Melbourne" },
    { ...selection(), extra: true }, Object.create(selection())]) {
    await assert.rejects(
      () => materializeProgramSidecars({ ...valid, selection: value }),
      /selection/);
  }
  const withSymbol = { ...selection(), [Symbol("invalid")]: true };
  await assert.rejects(
    () => materializeProgramSidecars({ ...valid, selection: withSymbol }),
    /selection/);
  const withAccessor = { artifact: sidecar() };
  Object.defineProperty(withAccessor, "timeZone", {
    get: () => "Australia/Melbourne",
    enumerable: true,
  });
  await assert.rejects(
    () => materializeProgramSidecars({ ...valid, selection: withAccessor }),
    /selection/);

  for (const timeZone of ["", "UTC", "Etc/UTC", "bad\0zone"]) {
    await assert.rejects(
      () => materializeProgramSidecars({
        ...valid,
        selection: { ...selection(), timeZone },
      }),
      /non-UTC TZ/);
  }
  await assert.rejects(
    () => materializeProgramSidecars({ ...valid, selection: { ...selection(), timeZone: null } }),
    /artifact contract/);
  await assert.rejects(
    () => materializeProgramSidecars({ ...valid, selection: { ...selection(), artifacts: [] } }),
    /requires artifacts/);
  for (const artifact of [
    sidecar({ role: "application" }),
    sidecar({ mediaType: "application/json" }),
    sidecar({ schemaVersion: 2 }),
    sidecar({ relativePath: "publish/app.tz-info" }),
    symbols("type-symbols", { relativePath: "publish/app.type-symbols" }),
    symbols("method-symbols", { mediaType: "application/octet-stream" }),
    symbols("type-symbols", { schemaVersion: 2 }),
  ]) {
    await assert.rejects(
      () => materializeProgramSidecars({
        ...valid,
        selection: { ...selection(), artifacts: [artifact] },
      }),
      /artifact contract/);
  }
});

// Issue 225: every sidecar mounts under one directory by its own name, and one failure releases the rest.
test("mounts every selected sidecar by name and releases them in reverse", async () => {
  const typeBytes = Uint8Array.of(1, 2);
  const methodBytes = Uint8Array.of(3);
  const contents = new Map([
    ["type-symbols", typeBytes], ["method-symbols", methodBytes], ["timezone-data", sourceBytes],
  ]);
  const artifacts = [
    symbols("type-symbols", { sha256: digest(typeBytes) }),
    symbols("method-symbols", { sha256: digest(methodBytes) }),
    sidecar(),
  ];
  const calls = [];
  const action = await materializeProgramSidecars({
    ...request(),
    selection: { timeZone: "Australia/Melbourne", artifacts },
    readArtifact: async artifact => contents.get(artifact.role),
    mountReadOnlyFile(mount) {
      calls.push(`mount ${mount.guestPath}`);
      return () => calls.push(`release ${mount.guestPath}`);
    },
  });
  action.release();
  assert.deepEqual(calls, [
    "mount /netwasm-sidecars/app.wasm.type-symbols",
    "mount /netwasm-sidecars/app.wasm.method-symbols",
    "mount /netwasm-sidecars/app.wasm.tz-info",
    "release /netwasm-sidecars/app.wasm.tz-info",
    "release /netwasm-sidecars/app.wasm.method-symbols",
    "release /netwasm-sidecars/app.wasm.type-symbols",
  ]);

  const symbolsOnly = await materializeProgramSidecars({
    ...request(),
    selection: { timeZone: null, artifacts: artifacts.slice(0, 1) },
    readArtifact: async () => typeBytes,
  });
  assert.equal(typeof symbolsOnly.release, "function");

  calls.length = 0;
  await assert.rejects(() => materializeProgramSidecars({
    ...request(),
    selection: { timeZone: "Australia/Melbourne", artifacts },
    readArtifact: async artifact => artifact.role === "timezone-data" ? Uint8Array.of(9) : contents.get(artifact.role),
    mountReadOnlyFile(mount) {
      calls.push(`mount ${mount.guestPath}`);
      return () => calls.push(`release ${mount.guestPath}`);
    },
  }), /integrity check failed/);
  assert.deepEqual(calls, [
    "mount /netwasm-sidecars/app.wasm.type-symbols",
    "mount /netwasm-sidecars/app.wasm.method-symbols",
    "release /netwasm-sidecars/app.wasm.method-symbols",
    "release /netwasm-sidecars/app.wasm.type-symbols",
  ]);
});

// Issue 225: a symbols file that cannot be read or verified is not mounted, and the program runs
// without it; time zone data stays required.
test("skips a symbols sidecar it cannot read or verify and mounts the rest", async () => {
  const typeBytes = Uint8Array.of(1, 2);
  const methodBytes = Uint8Array.of(3);
  const artifacts = [
    symbols("type-symbols", { sha256: digest(typeBytes) }),
    symbols("method-symbols", { sha256: digest(methodBytes) }),
    sidecar(),
  ];
  const mounted = [];
  const action = await materializeProgramSidecars({
    ...request(),
    selection: { timeZone: "Australia/Melbourne", artifacts },
    async readArtifact(artifact) {
      if (artifact.role === "method-symbols") throw new Error("ENOENT");
      return artifact.role === "type-symbols" ? Uint8Array.of(9) : sourceBytes;
    },
    mountReadOnlyFile(mount) {
      mounted.push(mount.guestPath);
      return () => {};
    },
  });
  assert.deepEqual(mounted, ["/netwasm-sidecars/app.wasm.tz-info"]);
  action.release();

  const controller = new AbortController();
  await assert.rejects(() => materializeProgramSidecars({
    ...request(),
    signal: controller.signal,
    selection: { timeZone: null, artifacts: artifacts.slice(0, 1) },
    async readArtifact() {
      controller.abort();
      return typeBytes;
    },
  }), error => error.name === "AbortError");
});

test("rejects invalid or tampered bytes before mounting", async () => {
  const valid = request();
  for (const bytes of [null, [], sourceBytes.buffer]) {
    await assert.rejects(
      () => materializeProgramSidecars({ ...valid, readArtifact: async () => bytes }),
      /reader returned invalid bytes/);
  }
  for (const value of [null, "A".repeat(64), "a".repeat(63), "g".repeat(64)]) {
    await assert.rejects(
      () => materializeProgramSidecars({ ...valid, hashBytes: async () => value }),
      /hasher returned an invalid digest/);
  }
  await assert.rejects(
    () => materializeProgramSidecars({
      ...valid,
      hashBytes: async () => "0".repeat(64),
    }),
    /integrity check failed/);
});

test("rejects a TZ the sidecar does not contain before mounting", async () => {
  const other = timeZoneContainer([["America/New_York", 0]]);
  const selected = selectProgramSidecars({
    runtimeFeatures: ["local-time"],
    artifacts: [application(), sidecar({ sha256: digest(other) })],
    environment: [{ name: "TZ", value: "Australia/Melbourne" }],
  });
  await assert.rejects(
    () => materializeProgramSidecars({
      selection: selected,
      readArtifact: async () => other,
      hashBytes: async bytes => digest(bytes),
      mountReadOnlyFile: () => assert.fail("must not mount"),
    }),
    error => error instanceof TypeError
      && error.message.startsWith("TZ 'Australia/Melbourne' is not in this deployment's time zone data.")
      && error.message.includes("NetWasmTimeZone"));
});

test("requires a synchronous release act from the read-only mount", async () => {
  for (const release of [null, {}, Promise.resolve(() => {})]) {
    await assert.rejects(
      () => materializeProgramSidecars({
        ...request(),
        mountReadOnlyFile: () => release,
      }),
      /invalid release action/);
  }
});

test("honors cancellation before reading, after reading, and after hashing", async () => {
  const calls = [];
  const beforeRead = new AbortController();
  beforeRead.abort();
  await assert.rejects(() => materializeProgramSidecars({
    ...request(),
    signal: beforeRead.signal,
    readArtifact: async () => { calls.push("unexpected read"); return sourceBytes; },
  }), error => error.name === "AbortError");

  const afterRead = new AbortController();
  await assert.rejects(() => materializeProgramSidecars({
    ...request(),
    signal: afterRead.signal,
    async readArtifact() {
      calls.push("read");
      afterRead.abort();
      return sourceBytes;
    },
  }), error => error.name === "AbortError");

  const afterHash = new AbortController();
  await assert.rejects(() => materializeProgramSidecars({
    ...request(),
    signal: afterHash.signal,
    hashBytes(bytes) {
      calls.push("hash");
      afterHash.abort();
      return digest(bytes);
    },
  }), error => error.name === "AbortError");
  assert.deepEqual(calls, ["read", "hash"]);
});

function request() {
  return {
    selection: selection(),
    readArtifact: async () => sourceBytes,
    hashBytes: async bytes => digest(bytes),
    mountReadOnlyFile: () => () => {},
  };
}
