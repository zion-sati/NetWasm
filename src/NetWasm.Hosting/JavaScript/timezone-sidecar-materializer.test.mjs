import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { selectTimeZoneSidecar } from "./timezone-sidecar-selector.mjs";
import { materializeTimeZoneSidecar } from "./timezone-sidecar-materializer.mjs";

const sourceBytes = Uint8Array.of(1, 2, 3, 4);
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
const selection = () => selectTimeZoneSidecar({
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
  const action = await materializeTimeZoneSidecar({
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
      assert.equal(Object.isFrozen(request), true);
      mountedBytes = request.bytes;
      return () => calls.push(["release"]);
    },
  });

  assert.equal(Object.isFrozen(action), true);
  assert.equal(action.code, "host.timezone-mount");
  assert.match(action.message, /release the timezone sidecar mount/);
  assert.deepEqual(calls[0][1], selected.artifact);
  assert.notEqual(calls[0][1], selected.artifact);
  assert.equal(calls[0][2], null);
  assert.deepEqual(calls.map(value => value[0]), ["read", "hash", "mount"]);
  assert.deepEqual(mountedBytes, sourceBytes);
  assert.notEqual(mountedBytes, sourceBytes);
  assert.notEqual(hashedBytes, sourceBytes);
  assert.notEqual(hashedBytes, mountedBytes);
  await action.release();
  assert.deepEqual(calls.map(value => value[0]), ["read", "hash", "mount", "release"]);

  transport.fill(9);
  assert.deepEqual(mountedBytes, Uint8Array.of(1, 2, 3, 4));
});

test("returns null without invoking transport, hashing, or mounting", async () => {
  const unexpected = () => { throw new Error("must not execute"); };
  assert.equal(await materializeTimeZoneSidecar({
    selection: null,
    readArtifact: unexpected,
    hashBytes: unexpected,
    mountReadOnlyFile: unexpected,
  }), null);
});

test("rejects invalid materialization requests and collaborators before I/O", async () => {
  const valid = request();
  for (const value of [null, 1, [], {}, { ...valid, extra: true }, Object.create(valid)]) {
    await assert.rejects(() => materializeTimeZoneSidecar(value), /request/);
  }
  const withSymbol = { ...valid, [Symbol("invalid")]: true };
  await assert.rejects(() => materializeTimeZoneSidecar(withSymbol), /request/);
  const withAccessor = { ...valid };
  Object.defineProperty(withAccessor, "selection", { get: selection, enumerable: true });
  await assert.rejects(() => materializeTimeZoneSidecar(withAccessor), /request/);

  for (const key of ["readArtifact", "hashBytes", "mountReadOnlyFile"]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({ ...valid, [key]: null }),
      /required/);
  }
  await assert.rejects(
    () => materializeTimeZoneSidecar({ ...valid, signal: {} }),
    /AbortSignal/);
});

test("requires one exact non-UTC timezone selection", async () => {
  const valid = request();
  for (const value of [1, [], {}, { timeZone: "Australia/Melbourne" },
    { ...selection(), extra: true }, Object.create(selection())]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({ ...valid, selection: value }),
      /selection/);
  }
  const withSymbol = { ...selection(), [Symbol("invalid")]: true };
  await assert.rejects(
    () => materializeTimeZoneSidecar({ ...valid, selection: withSymbol }),
    /selection/);
  const withAccessor = { artifact: sidecar() };
  Object.defineProperty(withAccessor, "timeZone", {
    get: () => "Australia/Melbourne",
    enumerable: true,
  });
  await assert.rejects(
    () => materializeTimeZoneSidecar({ ...valid, selection: withAccessor }),
    /selection/);

  for (const timeZone of [null, "", "UTC", "Etc/UTC", "bad\0zone"]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({
        ...valid,
        selection: { ...selection(), timeZone },
      }),
      /non-UTC TZ/);
  }
  for (const artifact of [
    sidecar({ role: "application" }),
    sidecar({ mediaType: "application/json" }),
    sidecar({ schemaVersion: 2 }),
    sidecar({ relativePath: "publish/app.tz-info" }),
  ]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({
        ...valid,
        selection: { ...selection(), artifact },
      }),
      /artifact contract/);
  }
});

test("rejects invalid or tampered bytes before mounting", async () => {
  const valid = request();
  for (const bytes of [null, [], sourceBytes.buffer]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({ ...valid, readArtifact: async () => bytes }),
      /reader returned invalid bytes/);
  }
  for (const value of [null, "A".repeat(64), "a".repeat(63), "g".repeat(64)]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({ ...valid, hashBytes: async () => value }),
      /hasher returned an invalid digest/);
  }
  await assert.rejects(
    () => materializeTimeZoneSidecar({
      ...valid,
      hashBytes: async () => "0".repeat(64),
    }),
    /integrity check failed/);
});

test("requires a synchronous release act from the read-only mount", async () => {
  for (const release of [null, {}, Promise.resolve(() => {})]) {
    await assert.rejects(
      () => materializeTimeZoneSidecar({
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
  await assert.rejects(() => materializeTimeZoneSidecar({
    ...request(),
    signal: beforeRead.signal,
    readArtifact: async () => { calls.push("unexpected read"); return sourceBytes; },
  }), error => error.name === "AbortError");

  const afterRead = new AbortController();
  await assert.rejects(() => materializeTimeZoneSidecar({
    ...request(),
    signal: afterRead.signal,
    async readArtifact() {
      calls.push("read");
      afterRead.abort();
      return sourceBytes;
    },
  }), error => error.name === "AbortError");

  const afterHash = new AbortController();
  await assert.rejects(() => materializeTimeZoneSidecar({
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
