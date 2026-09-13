import assert from "node:assert/strict";
import test from "node:test";
import { resourceDisposeSymbol } from "./resource-disposal.mjs";
import { releaseRawProviderResource } from "./raw-provider-resource-releaser.mjs";

test("releases own and inherited provider hooks with the resource receiver", () => {
  const calls = [];
  const own = {
    [resourceDisposeSymbol]() { calls.push(["own", this]); },
  };
  class Inherited {
    [resourceDisposeSymbol]() { calls.push(["inherited", this]); }
  }
  const inherited = new Inherited();
  releaseRawProviderResource({ type: 1, value: own });
  releaseRawProviderResource({ type: 2, value: inherited });
  assert.deepEqual(calls, [["own", own], ["inherited", inherited]]);
});

test("accepts a callable resource and an absent release hook", () => {
  const value = () => {};
  assert.equal(releaseRawProviderResource({ type: 0, value }), undefined);
  assert.equal(releaseRawProviderResource({ type: Number.MAX_SAFE_INTEGER, value: {} }), undefined);
});

test("rejects invalid requests, types, resources and hooks", () => {
  for (const request of [
    null,
    [],
    {},
    { type: 0, value: {}, extra: true },
    Object.assign({ type: 0, value: {} }, { [Symbol("extra")]: true }),
    { type: -1, value: {} },
    { type: 1.5, value: {} },
    { type: 0, value: null },
    { type: 0, value: 1 },
    { type: 0, value: { [resourceDisposeSymbol]: 1 } },
    { type: 0, value: Object.defineProperty({}, resourceDisposeSymbol, { get() { return () => {}; } }) },
  ]) assert.throws(() => releaseRawProviderResource(request), TypeError);
});

test("preserves a provider release failure", () => {
  const failure = new Error("release failed");
  const value = { [resourceDisposeSymbol]() { throw failure; } };
  assert.throws(() => releaseRawProviderResource({ type: 0, value }), error => error === failure);
});
