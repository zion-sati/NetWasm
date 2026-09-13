import assert from "node:assert/strict";
import test from "node:test";
import { createRawProviderCallableBinder } from "./raw-provider-callable-binder.mjs";

class FileHandle {
  constructor(value) {
    this.value = value;
  }

  readValue(suffix) {
    return `${this.value}:${suffix}`;
  }

  static openValue(value) {
    return new FileHandle(value);
  }
}

const providers = () => ({
  "": { rootValue() { return this.marker; }, marker: 7 },
  "sample:raw@1/api": {
    FileHandle,
    marker: 41,
    readValue(value) { return this.marker + value; },
  },
});

const provider = (overrides = {}) => ({
  interface: "sample:raw@1/api",
  function: "read-value",
  javascriptName: "readValue",
  functionKind: "freestanding",
  resourceType: null,
  resourceName: null,
  resourceJavaScriptName: null,
  ...overrides,
});

const binding = (overrides = {}) => ({ provider: provider(), ...overrides });

test("binds freestanding and root provider functions with their owners", () => {
  const bind = createRawProviderCallableBinder({ providers: providers() });
  assert.equal(bind(binding())([1]), 42);
  assert.equal(bind(binding({ provider: provider({
    interface: "",
    function: "root-value",
    javascriptName: "rootValue",
  }) }))([]), 7);
});

test("binds constructor, static and method resource operations", () => {
  const bind = createRawProviderCallableBinder({ providers: providers() });
  const constructor = bind(binding({ provider: provider({
    function: "[constructor]file-handle",
    functionKind: "constructor",
    javascriptName: "FileHandle",
    resourceJavaScriptName: "FileHandle",
    resourceName: "file-handle",
    resourceType: 9,
  }) }));
  const value = constructor(["created"]);
  assert.ok(value instanceof FileHandle);
  assert.equal(value.value, "created");

  const staticCall = bind(binding({ provider: provider({
    function: "[static]file-handle.open-value",
    functionKind: "static",
    javascriptName: "openValue",
    resourceJavaScriptName: "FileHandle",
    resourceName: "file-handle",
    resourceType: 9,
  }) }));
  assert.equal(staticCall(["opened"]).value, "opened");

  const method = bind(binding({ provider: provider({
    function: "[method]file-handle.read-value",
    functionKind: "method",
    javascriptName: "readValue",
    resourceJavaScriptName: "FileHandle",
    resourceName: "file-handle",
    resourceType: 9,
  }) }));
  assert.equal(method([value, "tail"]), "created:tail");
});

test("rejects invalid factory and provider object shapes", () => {
  for (const request of [null, [], {}, { providers: null }, { providers: {}, extra: true }]) {
    assert.throws(() => createRawProviderCallableBinder(request), TypeError);
  }
  assert.throws(() => createRawProviderCallableBinder({
    providers: { get value() { return {}; } },
  }), /data properties/);
  assert.throws(() => createRawProviderCallableBinder({
    providers: Object.assign({}, { [Symbol("extra")]: true }),
  }), TypeError);
});

test("rejects malformed callable provider bindings without invoking members", () => {
  const bind = createRawProviderCallableBinder({ providers: providers() });
  for (const candidate of [null, [], {}, { provider: null }]) {
    assert.throws(() => bind(candidate), TypeError);
  }
  assert.throws(() => bind(Object.defineProperty({}, "provider", { get() { return provider(); } })),
    /data property/);
  for (const candidate of [
    null,
    [],
    { ...provider(), extra: true },
    Object.assign(provider(), { [Symbol("extra")]: true }),
    Object.defineProperty(provider(), "function", { get() { return "read-value"; } }),
  ]) {
    assert.throws(() => bind(binding({ provider: candidate })), TypeError);
  }
});

test("rejects missing and malformed freestanding provider identities", () => {
  const bind = createRawProviderCallableBinder({ providers: providers() });
  for (const changes of [
    { interface: null },
    { function: "" },
    { javascriptName: "" },
    { functionKind: "future" },
    { resourceType: 1 },
    { resourceName: "file" },
    { resourceJavaScriptName: "File" },
  ]) {
    assert.throws(() => bind(binding({ provider: provider(changes) })), TypeError);
  }
  assert.throws(() => bind(binding({ provider: provider({ interface: "missing" }) })), /unavailable/);
  assert.throws(() => bind(binding({ provider: provider({ javascriptName: "missing" }) })), TypeError);
  assert.throws(() => createRawProviderCallableBinder({
    providers: { "sample:raw@1/api": Object.defineProperty({}, "readValue", { get() { return () => 1; } }) },
  })(binding()), /data property/);
  assert.throws(() => createRawProviderCallableBinder({
    providers: { "sample:raw@1/api": { readValue: 1 } },
  })(binding()), TypeError);
});

test("rejects malformed resource providers and calls", () => {
  const make = (kind, changes = {}) => binding({ provider: provider({
    function: `[${kind}]file-handle.${kind === "constructor" ? "" : "read-value"}`,
    functionKind: kind,
    javascriptName: kind === "constructor" ? "FileHandle" : "readValue",
    resourceJavaScriptName: "FileHandle",
    resourceName: "file-handle",
    resourceType: 9,
    ...changes,
  }) });
  const bind = createRawProviderCallableBinder({ providers: providers() });
  for (const changes of [
    { resourceType: -1 },
    { resourceType: 1.5 },
    { resourceName: "" },
    { resourceJavaScriptName: "" },
  ]) assert.throws(() => bind(make("method", changes)), TypeError);
  assert.throws(() => bind(make("constructor", { javascriptName: "Other" })), /constructor identity/);
  assert.throws(() => createRawProviderCallableBinder({
    providers: { "sample:raw@1/api": { FileHandle: null } },
  })(make("method")), /unavailable/);
  assert.throws(() => createRawProviderCallableBinder({
    providers: { "sample:raw@1/api": Object.defineProperty({}, "FileHandle", { get() { return FileHandle; } }) },
  })(make("method")), /data property/);
  assert.throws(() => bind(make("static")), TypeError);

  class MissingMethod {}
  assert.throws(() => createRawProviderCallableBinder({
    providers: { "sample:raw@1/api": { FileHandle: MissingMethod } },
  })(make("method")), TypeError);

  class AccessorMethod {}
  Object.defineProperty(AccessorMethod.prototype, "readValue", { get() { return () => 1; } });
  assert.throws(() => createRawProviderCallableBinder({
    providers: { "sample:raw@1/api": { FileHandle: AccessorMethod } },
  })(make("method")), /data property/);

  const method = bind(make("method"));
  assert.throws(() => method(null), /parameters/);
  assert.throws(() => method([]), /receiver/);
  assert.throws(() => method([{}]), /receiver/);
  const altered = new FileHandle("x");
  Object.defineProperty(altered, "readValue", { get() { return () => 1; } });
  assert.throws(() => method([altered]), /data property/);
  const nonCallable = new FileHandle("x");
  nonCallable.readValue = 1;
  assert.throws(() => method([nonCallable]), /method is invalid/);
  assert.throws(() => bind(make("constructor"))(null), /parameters/);
  assert.throws(() => bind(make("static" , { javascriptName: "openValue" }))(null), /parameters/);
});
