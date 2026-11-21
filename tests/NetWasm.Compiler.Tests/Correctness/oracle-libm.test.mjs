// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { loadOracleLibm } from "./oracle-libm.mjs";

const directory = new URL(".", import.meta.url);
const manifest = JSON.parse(await readFile(new URL("oracle-libm.json", directory), "utf8"));
const declarations = await readFile(
  new URL("../../../src/NetWasm.CoreLib/Runtime/RuntimeImports.Libm.cs", directory), "utf8");
const toolchain = JSON.parse(await readFile(new URL("../../../eng/toolchain.json", directory), "utf8"));

test("the modules provide exactly CoreLib's libc math imports and the ports' references, from the pinned Emscripten", () => {
  const declared = [...declarations.matchAll(/EntryPoint = "([a-z0-9]+)"/g)].map(match => match[1]);
  // A reference is a musl function CoreLib calls a managed port of instead of importing it.
  assert.deepEqual(manifest.references, ["exp2"]);
  assert.ok(manifest.references.every(name => !declared.includes(name)));
  assert.deepEqual(manifest.functions, [...declared, ...manifest.references]);
  assert.equal(manifest.emscripten, toolchain.emscripten);
});

for (const [target, pointer, second] of [["wasm32", 64, 80], ["wasm64", 64n, 80n]]) {
  test(`${target}: functions forward to musl, and modf and sincos write the program's memory`, async () => {
    const program = new WebAssembly.Memory(target === "wasm64"
      ? { initial: 1n, address: "i64" }
      : { initial: 1 });
    const libm = await loadOracleLibm(directory, target, () => program);

    assert.deepEqual(Object.keys(libm), manifest.functions);
    assert.equal(libm.log10(1000), 3);
    assert.equal(libm.exp2(10), 1024);
    assert.equal(libm.log2(8), 3);
    assert.equal(libm.pow(2, 10), 1024);
    assert.equal(libm.asinh(0), 0);
    assert.equal(libm.modf(-2.5, pointer), -0.5);
    assert.equal(new DataView(program.buffer).getFloat64(64, true), -2);
    assert.equal(libm.modff(7.25, pointer), 0.25);
    assert.equal(new DataView(program.buffer).getFloat32(64, true), 7);
    libm.sincos(Math.PI / 2, pointer, second);
    assert.equal(new DataView(program.buffer).getFloat64(64, true), 1);
    assert.equal(new DataView(program.buffer).getFloat64(80, true), Math.cos(Math.PI / 2));
    libm.sincosf(0, pointer, second);
    assert.equal(new DataView(program.buffer).getFloat32(64, true), 0);
    assert.equal(new DataView(program.buffer).getFloat32(80, true), 1);
  });
}

test("an unknown target or a missing memory accessor is rejected", async () => {
  await assert.rejects(loadOracleLibm(directory, "wasm16", () => null), TypeError);
  await assert.rejects(loadOracleLibm(directory, "wasm32", null), TypeError);
});
