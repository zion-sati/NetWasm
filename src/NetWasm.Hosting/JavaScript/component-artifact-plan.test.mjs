import assert from "node:assert/strict";
import test from "node:test";
import { createComponentArtifactPlan } from "./component-artifact-plan.mjs";

const digest = character => character.repeat(64);
const artifact = (relativePath, role, mediaType, sha256 = digest("a"), schemaVersion = null) => ({
  relativePath,
  role,
  mediaType,
  sha256,
  schemaVersion,
});
const artifacts = () => [
  artifact("publish/My App.wasm", "application", "application/wasm", digest("1")),
  artifact("receipts/compiler.json", "compiler-receipt", "application/json", digest("2"), 1),
  artifact("publish/My App.wasm.adapter.mjs", "component-adapter", "text/javascript", digest("3")),
  artifact("publish/program-component.js", "component-javascript", "text/javascript", digest("4")),
  artifact("publish/program-component.core2.wasm", "component-core-module", "application/wasm", digest("5")),
  artifact("publish/program-component.core.wasm", "component-core-module", "application/wasm", digest("6")),
];

test("builds one immutable exact component artifact plan", () => {
  const source = artifacts();
  const plan = createComponentArtifactPlan({ deploymentKind: "component", artifacts: source });

  assert.equal(Object.isFrozen(plan), true);
  assert.equal(Object.isFrozen(plan.application), true);
  assert.equal(Object.isFrozen(plan.adapter), true);
  assert.equal(Object.isFrozen(plan.generatedModule), true);
  assert.equal(Object.isFrozen(plan.coreModules), true);
  assert.equal(plan.application.relativePath, "publish/My App.wasm");
  assert.equal(plan.adapter.relativePath, "publish/My App.wasm.adapter.mjs");
  assert.equal(plan.generatedModule.relativePath, "publish/program-component.js");
  assert.deepEqual(plan.coreModules.map(value => value.relativePath), [
    "publish/program-component.core.wasm",
    "publish/program-component.core2.wasm",
  ]);
  assert.equal(
    plan.resolveCoreModule("program-component.core2.wasm"),
    plan.coreModules[1]);
  assert.equal(Object.hasOwn(plan, "compilerReceipt"), false);

  source[0].relativePath = "changed.wasm";
  source.length = 0;
  assert.equal(plan.application.relativePath, "publish/My App.wasm");
  assert.equal(plan.resolveCoreModule("program-component.core.wasm"), plan.coreModules[0]);
});

test("uses the same artifact contract for browser deployment", () => {
  const plan = createComponentArtifactPlan({ deploymentKind: "browser", artifacts: artifacts().reverse() });
  assert.equal(plan.generatedModule.role, "component-javascript");
});

test("rejects non-component deployment kinds and invalid plan requests", () => {
  for (const deploymentKind of [undefined, "", "raw", "Component", "local"]) {
    assert.throws(() => createComponentArtifactPlan({ deploymentKind, artifacts: artifacts() }), /kind/);
  }
  for (const request of [null, 1, [], {}, { deploymentKind: "component" },
    { deploymentKind: "component", artifacts: artifacts(), extra: true },
    Object.create({ deploymentKind: "component", artifacts: artifacts() })]) {
    assert.throws(() => createComponentArtifactPlan(request), /request/);
  }
  const withSymbol = { deploymentKind: "component", artifacts: artifacts() };
  withSymbol[Symbol("invalid")] = true;
  assert.throws(() => createComponentArtifactPlan(withSymbol), /request/);
  const withAccessor = { deploymentKind: "component" };
  Object.defineProperty(withAccessor, "artifacts", { get: artifacts, enumerable: true });
  assert.throws(() => createComponentArtifactPlan(withAccessor), /request/);
  for (const value of [null, {}, "artifacts", []]) {
    assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: value }), /artifacts/);
  }
});

test("requires the exact role cardinalities", () => {
  for (const role of ["application", "component-adapter", "component-javascript"]) {
    const missing = artifacts().filter(value => value.role !== role);
    assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: missing }), new RegExp(role));
    const duplicate = artifacts();
    duplicate.push({ ...duplicate.find(value => value.role === role), relativePath: `duplicate-${role}` });
    assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: duplicate }), new RegExp(role));
  }
  const withoutCore = artifacts().filter(value => value.role !== "component-core-module");
  assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: withoutCore }), /at least one core/);
});

test("requires exact media types for every runtime role", () => {
  for (const role of ["application", "component-adapter", "component-javascript", "component-core-module"]) {
    const values = artifacts();
    values.find(value => value.role === role).mediaType = "application/octet-stream";
    assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: values }), new RegExp(role));
  }
});

test("rejects duplicate paths and ambiguous core-module leaf names", () => {
  const duplicatePath = artifacts();
  duplicatePath[1].relativePath = duplicatePath[0].relativePath;
  assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: duplicatePath }), /path is duplicated/);

  const duplicateLeaf = artifacts();
  duplicateLeaf.push(artifact(
    "other/program-component.core.wasm",
    "component-core-module",
    "application/wasm"));
  assert.throws(() => createComponentArtifactPlan({ deploymentKind: "component", artifacts: duplicateLeaf }), /name.*duplicated/);
});

test("rejects malformed artifact objects and values", () => {
  const valid = artifacts()[0];
  const invalidShapes = [null, [], "artifact", {}, { ...valid, extra: true }, Object.create(valid)];
  for (const value of invalidShapes) {
    assertInvalidArtifact(value, /artifact/);
  }
  const withSymbol = { ...valid, [Symbol("invalid")]: true };
  assertInvalidArtifact(withSymbol, /artifact/);
  const withAccessor = { ...valid };
  Object.defineProperty(withAccessor, "role", { get: () => "application", enumerable: true });
  assertInvalidArtifact(withAccessor, /shape/);

  for (const relativePath of [null, "", " app.wasm", "app.wasm ", "/app.wasm", "C:app.wasm",
    "a\\app.wasm", "a//app.wasm", "a/./app.wasm", "a/../app.wasm", "app\n.wasm"]) {
    assertInvalidArtifact({ ...valid, relativePath }, /path/);
  }
  for (const role of [null, "", "Application", "bad_role", "bad role"]) {
    assertInvalidArtifact({ ...valid, role }, /role/);
  }
  for (const mediaType of [null, "", "wasm", " application/wasm", "application/wasm ", "application/\nwasm"]) {
    assertInvalidArtifact({ ...valid, mediaType }, /media type/);
  }
  for (const sha256 of [null, "", digest("A"), "a".repeat(63), "g".repeat(64)]) {
    assertInvalidArtifact({ ...valid, sha256 }, /digest/);
  }
  for (const schemaVersion of [undefined, 0, -1, 1.5, "1"]) {
    assertInvalidArtifact({ ...valid, schemaVersion }, /schema version/);
  }
});

test("resolves only exact canonical core-module leaf names", () => {
  const plan = createComponentArtifactPlan({ deploymentKind: "component", artifacts: artifacts() });
  for (const name of [null, undefined, "", "path/program-component.core.wasm",
    "path\\program-component.core.wasm", "C:program-component.core.wasm", ".", "..", "bad\nname.wasm"]) {
    assert.throws(() => plan.resolveCoreModule(name), /lookup name/);
  }
  for (const name of ["program-component.CORE.wasm", "other.core.wasm"]) {
    assert.throws(() => plan.resolveCoreModule(name), /unavailable/);
  }
});

function assertInvalidArtifact(value, expectation) {
  const values = artifacts();
  values[0] = value;
  assert.throws(
    () => createComponentArtifactPlan({ deploymentKind: "component", artifacts: values }),
    expectation);
}
