// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { runtimeFeatureIds } from "./runtime-feature-ids.mjs";

const compilerFeatureIdsSource = new URL(
  "../../NetWasm.Compiler.Core/NetWasmRuntimeFeatureIds.cs",
  import.meta.url);

test("runtime feature ids are frozen, canonical and distinct", () => {
  assert.equal(Object.isFrozen(runtimeFeatureIds), true);
  assert.deepEqual([...runtimeFeatureIds].sort(), runtimeFeatureIds);
  assert.equal(new Set(runtimeFeatureIds).size, runtimeFeatureIds.length);
});

test("runtime feature ids mirror every compiler runtime feature constant", async () => {
  const source = await readFile(compilerFeatureIdsSource, "utf8");
  const compilerIds = [...source.matchAll(/public const string \w+ = "([^"]+)";/gu)]
    .map(([, id]) => id)
    .sort();

  assert.notEqual(compilerIds.length, 0);
  assert.deepEqual(runtimeFeatureIds, compilerIds);
});
