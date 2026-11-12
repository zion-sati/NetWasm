import assert from "node:assert/strict";
import test from "node:test";
import { selectProgramSidecars } from "./program-sidecar-selector.mjs";

const digest = character => character.repeat(64);
const artifact = (relativePath, role, mediaType, sha256, schemaVersion = null) => ({
  relativePath,
  role,
  mediaType,
  sha256,
  schemaVersion,
});
const application = () => artifact(
  "publish/My App.wasm", "application", "application/wasm", digest("a"));
const sidecar = () => artifact(
  "publish/My App.wasm.tz-info", "timezone-data", "application/octet-stream", digest("b"), 1);
const request = (overrides = {}) => ({
  runtimeFeatures: ["local-time"],
  artifacts: [application(), sidecar()],
  environment: [{ name: "TZ", value: "Australia/Melbourne" }],
  ...overrides,
});

test("selects and snapshots one explicit non-UTC timezone sidecar", () => {
  const source = request();
  const selection = selectProgramSidecars(source);

  assert.equal(Object.isFrozen(selection), true);
  assert.equal(Object.isFrozen(selection.artifacts), true);
  assert.equal(Object.isFrozen(selection.artifacts[0]), true);
  assert.equal(selection.timeZone, "Australia/Melbourne");
  assert.deepEqual(selection.artifacts, [source.artifacts[1]]);
  assert.notEqual(selection.artifacts[0], source.artifacts[1]);

  source.environment[0].value = "UTC";
  source.artifacts[1].relativePath = "changed.tz-info";
  assert.equal(selection.timeZone, "Australia/Melbourne");
  assert.equal(selection.artifacts[0].relativePath, "publish/My App.wasm.tz-info");
});

// Issue 225: a program's symbols sidecars are mounted whenever its deployment has them.
test("selects the symbols sidecars with or without time zone data", () => {
  const typeSymbols = artifact("publish/My App.wasm.type-symbols", "type-symbols",
    "application/vnd.netwasm.type-symbols;version=1", digest("c"), 1);
  const methodSymbols = artifact("publish/My App.wasm.method-symbols", "method-symbols",
    "application/vnd.netwasm.method-symbols;version=1", digest("d"), 1);

  const withoutTimeZone = selectProgramSidecars(request({
    runtimeFeatures: [], artifacts: [application(), typeSymbols, methodSymbols], environment: [],
  }));
  assert.equal(withoutTimeZone.timeZone, null);
  assert.deepEqual(withoutTimeZone.artifacts, [typeSymbols, methodSymbols]);

  const withTimeZone = selectProgramSidecars(request({ artifacts: [application(), sidecar(), typeSymbols] }));
  assert.equal(withTimeZone.timeZone, "Australia/Melbourne");
  assert.deepEqual(withTimeZone.artifacts, [typeSymbols, sidecar()]);

  for (const invalid of [
    { ...typeSymbols, relativePath: "publish/Other.wasm.type-symbols" },
    { ...typeSymbols, mediaType: "application/json" },
    { ...methodSymbols, schemaVersion: 2 },
  ]) {
    assert.throws(() => selectProgramSidecars(request({ artifacts: [application(), invalid] })),
      /adjacent schema-1 deployment contract/);
  }
  assert.throws(() => selectProgramSidecars(request({
    artifacts: [application(), typeSymbols, { ...typeSymbols, relativePath: "x.wasm.type-symbols" }],
  })), /only one type-symbols artifact/);
});

test("does not select a sidecar for unreachable, absent, or canonical UTC local time", () => {
  assert.equal(selectProgramSidecars(request({ runtimeFeatures: [], artifacts: [application()] })), null);
  assert.equal(selectProgramSidecars(request({ environment: [] })), null);
  assert.equal(selectProgramSidecars(request({ environment: [{ name: "TZ", value: "UTC" }] })), null);
  assert.equal(selectProgramSidecars(request({ environment: [{ name: "TZ", value: "Etc/UTC" }] })), null);
  assert.equal(selectProgramSidecars(request({
    environment: [{ name: "OTHER", value: "" }],
  })), null);
});

test("rejects invalid selection request shapes", () => {
  for (const value of [null, 1, [], {},
    { runtimeFeatures: [], artifacts: [application()] },
    { ...request(), extra: true }, Object.create(request())]) {
    assert.throws(() => selectProgramSidecars(value), /request/);
  }

  const withSymbol = { ...request(), [Symbol("invalid")]: true };
  assert.throws(() => selectProgramSidecars(withSymbol), /request/);
  const withAccessor = { artifacts: [application()], environment: [] };
  Object.defineProperty(withAccessor, "runtimeFeatures", { get: () => [], enumerable: true });
  assert.throws(() => selectProgramSidecars(withAccessor), /request/);

  const nullPrototype = Object.assign(Object.create(null), request({ environment: [] }));
  assert.equal(selectProgramSidecars(nullPrototype), null);
});

test("requires an exact supported runtime-feature inventory", () => {
  for (const runtimeFeatures of [null, {}, "local-time"]) {
    assert.throws(() => selectProgramSidecars(request({ runtimeFeatures })), /features.*explicit/);
  }
  for (const runtimeFeatures of [[null], [""], ["Local-Time"], ["bad_feature"]]) {
    assert.throws(() => selectProgramSidecars(request({ runtimeFeatures })), /feature is invalid/);
  }
  assert.throws(() => selectProgramSidecars(request({ runtimeFeatures: ["future"] })), /unsupported/);
  assert.throws(
    () => selectProgramSidecars(request({ runtimeFeatures: ["local-time", "local-time"] })),
    /unique/);
});

test("requires exact unique environment entries and rejects an empty TZ", () => {
  for (const environment of [null, {}, "TZ"]) {
    assert.throws(() => selectProgramSidecars(request({ environment })), /environment.*explicit/);
  }
  for (const entry of [null, [], "TZ", {}, { name: "TZ" },
    { name: "TZ", value: "UTC", extra: true }, Object.create({ name: "TZ", value: "UTC" })]) {
    assert.throws(() => selectProgramSidecars(request({ environment: [entry] })), /environment entry/);
  }
  const withSymbol = { name: "TZ", value: "UTC", [Symbol("invalid")]: true };
  assert.throws(() => selectProgramSidecars(request({ environment: [withSymbol] })), /environment entry/);
  const withAccessor = { name: "TZ" };
  Object.defineProperty(withAccessor, "value", { get: () => "UTC", enumerable: true });
  assert.throws(() => selectProgramSidecars(request({ environment: [withAccessor] })), /environment entry/);

  for (const name of [null, "", "BAD=NAME", "BAD\0NAME"]) {
    assert.throws(
      () => selectProgramSidecars(request({ environment: [{ name, value: "UTC" }] })),
      /name is invalid/);
  }
  for (const value of [null, "bad\0value"]) {
    assert.throws(
      () => selectProgramSidecars(request({ environment: [{ name: "TZ", value }] })),
      /value is invalid/);
  }
  assert.throws(() => selectProgramSidecars(request({ environment: [
    { name: "TZ", value: "UTC" }, { name: "TZ", value: "Etc/UTC" },
  ] })), /unique/);
  assert.throws(
    () => selectProgramSidecars(request({ environment: [{ name: "TZ", value: "" }] })),
    /must not be empty/);
});

test("requires one application and canonical unique artifact identities", () => {
  for (const artifacts of [null, {}, [], "application"]) {
    assert.throws(() => selectProgramSidecars(request({ artifacts })), /artifacts.*application/);
  }
  assert.throws(
    () => selectProgramSidecars(request({ artifacts: [sidecar()] })),
    /exactly one application/);
  assert.throws(
    () => selectProgramSidecars(request({ artifacts: [application(), application(), sidecar()] })),
    /paths must be unique/);
  const secondApplication = { ...application(), relativePath: "other.wasm" };
  assert.throws(
    () => selectProgramSidecars(request({ artifacts: [application(), secondApplication, sidecar()] })),
    /exactly one application/);

  const duplicatePath = { ...sidecar(), relativePath: application().relativePath };
  assert.throws(
    () => selectProgramSidecars(request({ artifacts: [application(), duplicatePath] })),
    /paths must be unique/);
  const secondSidecar = { ...sidecar(), relativePath: "other.wasm.tz-info" };
  assert.throws(
    () => selectProgramSidecars(request({ artifacts: [application(), sidecar(), secondSidecar] })),
    /only one timezone/);
});

test("binds timezone artifacts to local-time and the adjacent schema-1 contract", () => {
  assert.throws(
    () => selectProgramSidecars(request({ runtimeFeatures: [] })),
    /requires the local-time/);
  for (const invalidSidecar of [
    { ...sidecar(), relativePath: "other.wasm.tz-info" },
    { ...sidecar(), mediaType: "application/json" },
    { ...sidecar(), schemaVersion: 2 },
  ]) {
    assert.throws(
      () => selectProgramSidecars(request({ artifacts: [application(), invalidSidecar] })),
      /adjacent schema-1/);
  }
  assert.throws(
    () => selectProgramSidecars(request({ artifacts: [application()] })),
    /non-UTC TZ requires/);
});
