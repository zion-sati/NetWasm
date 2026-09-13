import assert from "node:assert/strict";
import test from "node:test";
import { createRawArtifactPlan } from "./raw-artifact-plan.mjs";

const digest = character => character.repeat(64);
const artifact = (relativePath, role, mediaType, sha256 = digest("a"), schemaVersion = null) => ({
  relativePath,
  role,
  mediaType,
  sha256,
  schemaVersion,
});
const artifacts = () => [
  artifact("publish/app.wasm", "application", "application/wasm", digest("1")),
  artifact("publish/app.raw-adapter.mjs", "raw-adapter", "text/javascript", digest("2")),
  artifact("publish/runtime-layout.json", "runtime-layout", "application/json", digest("3"), 2),
  artifact("publish/interop.json", "interop-manifest", "application/json", digest("4"), 1),
  artifact("publish/app.wasm.tz-info", "timezone-data", "application/octet-stream", digest("5"), 1),
];

test("selects one immutable manifested raw artifact closure", () => {
  const source = artifacts().reverse();
  const plan = createRawArtifactPlan({ deploymentKind: "raw", artifacts: source });
  assert.equal(Object.isFrozen(plan), true);
  assert.equal(plan.application.role, "application");
  assert.equal(plan.adapter.role, "raw-adapter");
  assert.equal(plan.runtimeLayout.role, "runtime-layout");
  assert.equal(plan.interopManifest.role, "interop-manifest");
  assert.equal(Object.isFrozen(plan.application), true);
  source[4].relativePath = "changed.wasm";
  assert.equal(plan.application.relativePath, "publish/app.wasm");
  assert.deepEqual(Object.keys(plan).sort(), ["adapter", "application", "interopManifest", "runtimeLayout"]);
});

test("accepts only the raw deployment kind and a nonempty artifact array", () => {
  for (const deploymentKind of [undefined, "", "component", "browser", "Raw"]) {
    assert.throws(() => createRawArtifactPlan({ deploymentKind, artifacts: artifacts() }), /kind/);
  }
  for (const value of [undefined, null, {}, []]) {
    assert.throws(() => createRawArtifactPlan({ deploymentKind: "raw", artifacts: value }), /artifacts/);
  }
});

test("requires every raw role exactly once with its exact media type", () => {
  for (const role of ["application", "raw-adapter", "runtime-layout", "interop-manifest"]) {
    const missing = artifacts().filter(value => value.role !== role);
    assert.throws(
      () => createRawArtifactPlan({ deploymentKind: "raw", artifacts: missing }),
      new RegExp(role));
    const duplicate = [...artifacts(), artifact(`duplicate-${role}`, role,
      role === "application" ? "application/wasm"
        : role === "raw-adapter" ? "text/javascript" : "application/json")];
    assert.throws(
      () => createRawArtifactPlan({ deploymentKind: "raw", artifacts: duplicate }),
      new RegExp(role));
    const wrongMedia = artifacts().map(value => value.role === role
      ? { ...value, mediaType: "application/octet-stream" }
      : value);
    assert.throws(
      () => createRawArtifactPlan({ deploymentKind: "raw", artifacts: wrongMedia }),
      new RegExp(role));
  }
});

test("rejects duplicate paths even when one role is unrelated", () => {
  const values = artifacts();
  values.push(artifact(values[0].relativePath, "receipt", "application/json"));
  assert.throws(
    () => createRawArtifactPlan({ deploymentKind: "raw", artifacts: values }),
    /path is duplicated/);
});

test("rejects non-exact request objects before reading artifacts", () => {
  for (const request of [null, 1, [], {}, { deploymentKind: "raw" },
    { deploymentKind: "raw", artifacts: artifacts(), extra: true },
    Object.create({ deploymentKind: "raw", artifacts: artifacts() })]) {
    assert.throws(() => createRawArtifactPlan(request), /request/);
  }
  const withSymbol = { deploymentKind: "raw", artifacts: artifacts(), [Symbol("bad")]: true };
  assert.throws(() => createRawArtifactPlan(withSymbol), /request/);
  const withAccessor = { deploymentKind: "raw" };
  Object.defineProperty(withAccessor, "artifacts", { enumerable: true, get: artifacts });
  assert.throws(() => createRawArtifactPlan(withAccessor), /request/);
});
