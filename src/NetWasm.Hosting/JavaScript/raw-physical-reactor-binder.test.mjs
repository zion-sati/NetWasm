import assert from "node:assert/strict";
import test from "node:test";
import { createRawPhysicalReactorBinder } from "./raw-physical-reactor-binder.mjs";

function callable(operation, overrides = {}) {
  const watch = operation === "watch";
  return {
    kind: "callable",
    target: "wasm32",
    physical: {
      module: "cm32p2|netwasm:runtime/reactor-host@1",
      name: operation,
    },
    coreSignature: {
      parameters: watch ? ["i32", "i32"] : ["i32"],
      results: [],
    },
    provider: {
      interface: "netwasm:runtime/reactor-host@1.0.0",
      function: operation,
      javascriptName: operation,
      functionKind: "freestanding",
      resourceType: null,
      resourceName: null,
      resourceJavaScriptName: null,
    },
    parameters: watch
      ? [
          { name: "ready", javascriptName: "ready", type: { kind: "owned-resource", resourceType: 7 } },
          { name: "token", javascriptName: "token", type: { kind: "u32" } },
        ]
      : [{ name: "token", javascriptName: "token", type: { kind: "u32" } }],
    result: null,
    canonicalSignature: {
      parameters: watch ? ["i32", "i32"] : ["i32"],
      result: null,
      flatParameters: watch ? ["i32", "i32"] : ["i32"],
      flatResults: [],
      indirectParameters: false,
      indirectResult: false,
    },
    parameterMemory: { size: watch ? 8 : 4, alignment: 4 },
    resultMemory: null,
    ...overrides,
  };
}

function fixture(overrides = {}) {
  const calls = [];
  const pollable = {};
  const store = {};
  const reactor = {
    module: "cm32p2|netwasm:runtime/reactor-host@1",
    assertAvailable() { calls.push("available"); },
    watch(value, token) { calls.push(["watch", value, token]); },
    cancel(token) { calls.push(["cancel", token]); },
  };
  const bind = createRawPhysicalReactorBinder({
    providers: {},
    reactor,
    readStore() { calls.push("store"); return store; },
    transferPollable(request) { calls.push(["transfer", request]); return pollable; },
    releasePollable(request) { calls.push(["release", request]); },
    ...overrides,
  });
  return { bind, calls, pollable, reactor, store };
}

test("transfers one owned pollable to the guarded reactor and normalizes u32", () => {
  const { bind, calls, pollable, store } = fixture();

  assert.equal(bind(callable("watch"))(9, -1), undefined);

  assert.deepEqual(calls, [
    "available",
    "store",
    ["transfer", { handle: 9, store, type: 7 }],
    ["watch", pollable, 0xffff_ffff],
  ]);
});

test("cancels without transferring a pollable or exposing the reactor result", () => {
  const { bind, calls } = fixture({
    reactor: {
      module: "cm32p2|netwasm:runtime/reactor-host@1",
      assertAvailable() { calls.push("available"); },
      watch() {},
      cancel(token) { calls.push(["cancel", token]); return true; },
    },
  });

  assert.equal(bind(callable("cancel"))(17), undefined);
  assert.deepEqual(calls, ["available", ["cancel", 17]]);
});

test("checks availability and token validity before ownership transfer", () => {
  const unavailable = new Error("unavailable");
  const first = fixture({
    reactor: {
      module: "cm32p2|netwasm:runtime/reactor-host@1",
      assertAvailable() { throw unavailable; },
      watch() {},
      cancel() {},
    },
  });
  assert.throws(() => first.bind(callable("watch"))(4, 1), error => error === unavailable);
  assert.deepEqual(first.calls, []);

  for (const [handle, token] of [[1.5, 1], [1, 0], [1, 0x8000_0000]]) {
    const current = fixture();
    assert.throws(() => current.bind(callable("watch"))(handle, token), TypeError);
    assert.deepEqual(current.calls, ["available"]);
  }
  const cancel = fixture();
  assert.throws(() => cancel.bind(callable("cancel"))(0), /nonzero/);
  assert.deepEqual(cancel.calls, ["available"]);
});

test("releases a transferred pollable exactly once when watch rejects", () => {
  const rejected = new Error("rejected");
  const current = fixture();
  current.reactor.watch = () => { throw rejected; };
  const watch = current.bind(callable("watch"));

  assert.throws(() => watch(3, 5), error => error === rejected);
  assert.deepEqual(current.calls, [
    "available",
    "store",
    ["transfer", { handle: 3, store: current.store, type: 7 }],
    ["release", { type: 7, value: current.pollable }],
  ]);
});

test("preserves both rejection and failed release", () => {
  const rejected = new Error("rejected");
  const release = new Error("release");
  const current = fixture({
    reactor: {
      module: "cm32p2|netwasm:runtime/reactor-host@1",
      assertAvailable() {},
      watch() { throw rejected; },
      cancel() {},
    },
    releasePollable() { throw release; },
  });

  assert.throws(() => current.bind(callable("watch"))(1, 2), error =>
    error instanceof AggregateError
      && error.errors[0] === rejected
      && error.errors[1] === release);
});

test("rejects malformed factories, bindings, identities, providers and signatures", () => {
  for (const request of [null, [], {}, { reactor: null, readStore() {}, releasePollable() {}, transferPollable() {} }]) {
    assert.throws(() => createRawPhysicalReactorBinder(request), TypeError);
  }
  const validFactory = {
    providers: {},
    reactor: {
      module: "cm32p2|netwasm:runtime/reactor-host@1",
      assertAvailable() {}, watch() {}, cancel() {},
    },
    readStore() {}, releasePollable() {}, transferPollable() {},
  };
  for (const mutation of [
    { providers: null },
    { providers: Object.create({}) },
    { providers: Object.defineProperty({}, "reactor", { get() { return {}; }, enumerable: true }) },
    { reactor: { ...validFactory.reactor, module: "" } },
    { reactor: { ...validFactory.reactor, watch: null } },
    { readStore: null },
    { releasePollable: null },
    { transferPollable: null },
  ]) assert.throws(() => createRawPhysicalReactorBinder({ ...validFactory, ...mutation }), TypeError);

  const bind = createRawPhysicalReactorBinder(validFactory);
  const watch = callable("watch");
  for (const binding of [
    null,
    {},
    { ...watch, kind: "resource" },
    { ...watch, target: "wasm128" },
    { ...watch, result: {} },
    { ...watch, resultMemory: {} },
    { ...watch, physical: { ...watch.physical, name: "other" } },
    { ...watch, physical: { ...watch.physical, module: "cm32p2|other" } },
    { ...watch, target: "wasm64" },
    { ...watch, provider: { ...watch.provider, interface: "app:fake@1/reactor-host" } },
    { ...watch, provider: { ...watch.provider, functionKind: "method" } },
    { ...watch, parameters: [] },
    { ...watch, parameters: [{ ...watch.parameters[0], name: "other" }, watch.parameters[1]] },
    { ...watch, parameters: [{ ...watch.parameters[0], type: { kind: "borrowed-resource", resourceType: 7 } }, watch.parameters[1]] },
    { ...watch, parameters: [watch.parameters[0], { ...watch.parameters[1], type: { kind: "s32" } }] },
    { ...watch, coreSignature: { parameters: ["i64", "i32"], results: [] } },
    { ...watch, canonicalSignature: { ...watch.canonicalSignature, indirectParameters: true } },
  ]) assert.throws(() => bind(binding), TypeError);

  const reserved = createRawPhysicalReactorBinder({
    ...validFactory,
    providers: { "netwasm:runtime/reactor-host@1.0.0": {} },
  });
  assert.throws(() => reserved(callable("cancel")), /reserved/);
});
