import assert from "node:assert/strict";
import test from "node:test";
import { createArtifactStrategyRegistry } from "./artifact-strategy-registry.mjs";

const registrations = () => [
  { kind: "raw", execute: () => "raw" },
  { kind: "component", execute: () => "component" },
  { kind: "browser", execute: () => "browser" },
];

test("resolves every registered deployment kind", () => {
  const values = registrations();
  const registry = createArtifactStrategyRegistry(values);
  assert.equal(Object.isFrozen(registry), true);
  for (const registration of values) {
    assert.equal(registry.resolve(registration.kind), registration.execute);
  }
});

test("supports exact non-empty host-local subsets", () => {
  const values = registrations();
  const local = createArtifactStrategyRegistry(values.slice(0, 2));
  assert.equal(local.resolve("raw"), values[0].execute);
  assert.equal(local.resolve("component"), values[1].execute);
  assert.throws(() => local.resolve("browser"), /unsupported/);

  const browser = createArtifactStrategyRegistry(values.slice(2));
  assert.equal(browser.resolve("browser"), values[2].execute);
  assert.throws(() => browser.resolve("raw"), /unsupported/);
});

test("snapshots registrations instead of retaining a mutable collection", () => {
  const values = registrations();
  const expected = values[0].execute;
  const registry = createArtifactStrategyRegistry(values);
  values.length = 0;
  assert.equal(registry.resolve("raw"), expected);
});

test("rejects invalid registration collections and shapes", () => {
  for (const values of [null, {}, "raw"]) {
    assert.throws(() => createArtifactStrategyRegistry(values), /registrations/);
  }
  for (const registration of [
    null,
    [],
    { kind: "raw" },
    { kind: "raw", execute: () => {}, extra: true },
    { kind: "raw", execute: 1 },
    { kind: "", execute: () => {} },
    { kind: "raw", execute: () => {}, [Symbol("bad")]: true },
    Object.defineProperty({ kind: "raw" }, "execute", {
      get() { return () => {}; },
      enumerable: true,
    }),
    Object.defineProperty({ kind: "raw" }, "execute", {
      value: () => {},
      enumerable: false,
    }),
  ]) {
    assert.throws(() => createArtifactStrategyRegistry([
      registration,
      ...registrations().slice(1),
    ]), /registration/);
  }
});

test("rejects empty, duplicate and unsupported registrations", () => {
  const valid = registrations();
  assert.throws(() => createArtifactStrategyRegistry([]), /must not be empty/);
  assert.throws(() => createArtifactStrategyRegistry([...valid, valid[0]]), /duplicated/);
  assert.throws(() => createArtifactStrategyRegistry([
    ...valid,
    { kind: "other", execute: () => {} },
  ]), /unsupported/);
  assert.throws(() => createArtifactStrategyRegistry([
    { ...valid[0], kind: "Raw" },
    ...valid.slice(1),
  ]), /unsupported/);
});

test("rejects invalid, missing and case-varied lookups without fallback", () => {
  const registry = createArtifactStrategyRegistry(registrations());
  for (const kind of [null, undefined, 1, ""]) {
    assert.throws(() => registry.resolve(kind), /required/);
  }
  for (const kind of ["Raw", "COMPONENT", "local", "other"]) {
    assert.throws(() => registry.resolve(kind), /unsupported/);
  }
});
