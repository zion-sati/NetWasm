// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";
import { resolveGlobalJavaScriptImport, resolveJavaScriptImport } from "./javascript-import-resolver.mjs";

test("a dotted name is a path through the module, called on the object it ends at", () => {
  const dom = { prefix: "class-", addClass(name) { return this.prefix + name; } };
  const module = Object.freeze({ AvaloniaDOM: dom, plain(value) { return value + 1; } });

  assert.equal(resolveJavaScriptImport(module, "AvaloniaDOM.addClass")("x"), "class-x");
  assert.equal(resolveJavaScriptImport(module, "plain")(1), 2);
  // A function found on a function, as a static method on a class.
  class Helper { static twice(value) { return value * 2; } }
  assert.equal(resolveJavaScriptImport({ Helper }, "Helper.twice")(3), 6);
});

test("a path that ends anywhere but at a function resolves to nothing", () => {
  const module = { AvaloniaDOM: { count: 3 }, empty: null, text: "value" };

  for (const name of ["missing", "AvaloniaDOM.count", "AvaloniaDOM.missing", "empty.call", "text.length", "missing.call"]) {
    assert.equal(resolveJavaScriptImport(module, name), null, name);
  }
  assert.equal(resolveJavaScriptImport(null, "plain"), null);
  assert.equal(resolveJavaScriptImport(undefined, "plain"), null);
  assert.equal(resolveJavaScriptImport("module", "plain"), null);
  assert.equal(resolveJavaScriptImport({ plain() {} }, 7), null);
});

test("a module-less import names a function on globalThis, as .NET's browser runtime finds it", () => {
  globalThis.netwasmResolverFixture = { scale: 10, times(value) { return value * this.scale; } };
  try {
    assert.equal(resolveGlobalJavaScriptImport("globalThis.netwasmResolverFixture.times")(4), 40);
    assert.equal(typeof resolveGlobalJavaScriptImport("globalThis.clearTimeout"), "function");
    for (const name of ["clearTimeout", "globalThis.", "globalThis", "globalThis.missingFunction", 3]) {
      assert.equal(resolveGlobalJavaScriptImport(name), null, String(name));
    }
  } finally {
    delete globalThis.netwasmResolverFixture;
  }
});
