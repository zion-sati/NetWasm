// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

import assert from "node:assert/strict";
import test from "node:test";
import { addUnprovidedWasiImports } from "./unprovided-wasi-imports.mjs";

const lookup = "wasi:sockets/ip-name-lookup@0.3.2";

test("a function of sockets, tls or the file system that the host lacks exists and fails when called", () => {
  const provided = () => 1;
  const imports = { "wasi:sockets/types@0.3": { create: provided }, other: { present: provided } };
  const completed = addUnprovidedWasiImports([
    { module: lookup, name: "resolve-addresses", kind: "function" },
    { module: "cm32p2|wasi:tls/client@0.3", name: "connect", kind: "function" },
    { module: "cm64p2|wasi:filesystem/types@0.3", name: "open-at", kind: "function" },
    { module: "wasi:sockets/types@0.3", name: "create", kind: "function" },
    { module: "wasi:sockets/types@0.3", name: "bind", kind: "function" },
  ], imports, "the linked corpus host");

  assert.throws(() => completed[lookup]["resolve-addresses"](),
    { message: `the linked corpus host does not provide ${lookup} resolve-addresses` });
  assert.throws(() => completed["cm32p2|wasi:tls/client@0.3"].connect(), /does not provide cm32p2\|wasi:tls\/client@0.3 connect/);
  assert.throws(() => completed["cm64p2|wasi:filesystem/types@0.3"]["open-at"](), /open-at/);
  // What the host provides stays as it is, beside what was added to the same interface.
  assert.equal(completed["wasi:sockets/types@0.3"].create, provided);
  assert.throws(() => completed["wasi:sockets/types@0.3"].bind(), /bind/);
  assert.equal(completed.other.present, provided);
  // The host's own objects are not changed.
  assert.deepEqual(Object.keys(imports), ["wasi:sockets/types@0.3", "other"]);
  assert.deepEqual(Object.keys(imports["wasi:sockets/types@0.3"]), ["create"]);
});

test("nothing else is filled in", () => {
  const completed = addUnprovidedWasiImports([
    { module: "wasi:http/types@0.3", name: "send", kind: "function" },
    { module: "netwasm.runtime.v1", name: "collect", kind: "function" },
    { module: lookup, name: "memory", kind: "memory" },
    { module: "x-wasi:sockets/types@0.3", name: "create", kind: "function" },
  ], {}, "the oracle");

  assert.deepEqual(completed, {});
});

test("the module's imports, the host's imports and the host's name are required", () => {
  assert.throws(() => addUnprovidedWasiImports(null, {}, "host"), TypeError);
  assert.throws(() => addUnprovidedWasiImports([], null, "host"), TypeError);
  assert.throws(() => addUnprovidedWasiImports([], 1, "host"), TypeError);
  assert.throws(() => addUnprovidedWasiImports([], {}, ""), TypeError);
  assert.throws(() => addUnprovidedWasiImports([], {}, 5), TypeError);
});
