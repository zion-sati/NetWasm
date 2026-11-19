// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Mirrors NetWasmRuntimeFeatureIds in NetWasm.Compiler.Core: every runtime feature the
// compiler may record, in ordinal order. runtime-feature-ids.test.mjs fails on drift.
export const runtimeFeatureIds = Object.freeze([
  "array-set-value",
  "ephemeron-handles",
  "finalization",
  "local-time",
  "object-clone",
  "object-generation",
  "random-seed",
  "reference-write-barrier",
  "structured-command-diagnostics",
  "uninitialized-arrays",
  "weak-handles",
  "young-collection",
]);
