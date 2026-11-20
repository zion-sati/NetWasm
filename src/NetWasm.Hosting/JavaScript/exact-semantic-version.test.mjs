// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { exactSemanticVersion } from "./exact-semantic-version.mjs";

const exact = new RegExp(`^${exactSemanticVersion}$`, "u");

test("a version is MAJOR.MINOR.PATCH with an optional prerelease and build, as WIT packages carry them", () => {
  for (const version of ["0.3.2", "10.0.1", "0.3.0-draft", "1.2.0-rc.1+build.7", "2.5.1+meta", "1.0.0-x-y.2"]) {
    assert.equal(exact.test(version), true, version);
  }
});

test("partial, ranged, leading-zero and empty-part versions are not exact", () => {
  for (const version of ["0.3", "1", "01.0.0", "0.02.1", "0.3.0-", "0.3.0+", "1.0.0-a..b", "1.0.0-dr_aft", "^1.0.0", "1.x.0"]) {
    assert.equal(exact.test(version), false, version);
  }
});
