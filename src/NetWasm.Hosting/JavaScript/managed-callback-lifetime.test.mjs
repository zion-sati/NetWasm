// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createManagedCallbackLifetime } from "./managed-callback-lifetime.mjs";

// Records registrations and lets a test collect them, as the engine would.
function recordingRegistry() {
  const registries = [];
  class Registry {
    constructor(cleanup) {
      this.cleanup = cleanup;
      this.held = [];
      registries.push(this);
    }

    register(target, held) {
      this.held.push({ target, held });
    }
  }
  return { Registry, registries };
}

test("keeps an adopted callback's handle until JavaScript collects the function", () => {
  const released = [];
  const { Registry, registries } = recordingRegistry();
  const lifetime = createManagedCallbackLifetime(handle => released.push(handle), Registry);
  const callback = lifetime.adopt(7, (left, right) => left + right);

  assert.equal(Object.isFrozen(callback), true);
  assert.equal(callback.function(2, 3), 5);
  callback.keep();
  assert.equal(registries[0].held[0].target, callback.function);
  assert.equal(registries[0].held[0].held, 7);
  assert.deepEqual(released, []);
  registries[0].cleanup(7);
  assert.deepEqual(released, [7]);
});

test("stops a dropped callback and releases handles quietly", () => {
  const lifetime = createManagedCallbackLifetime(() => { throw new Error("program ended"); }, undefined);
  const callback = lifetime.adopt(9, () => 1);

  callback.keep();
  callback.drop();
  assert.throws(() => callback.function(), /released when the import that passed it failed/);
  assert.doesNotThrow(() => lifetime.release(9));
  assert.throws(() => createManagedCallbackLifetime(null), TypeError);
});

test("releases through the registry it was given, or keeps handles without one", () => {
  const released = [];
  const lifetime = createManagedCallbackLifetime(handle => released.push(handle), null);
  lifetime.adopt(4, () => 0).keep();
  lifetime.release(4);
  assert.deepEqual(released, [4]);
});
