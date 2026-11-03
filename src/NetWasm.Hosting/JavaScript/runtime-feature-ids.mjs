// Mirrors NetWasmRuntimeFeatureIds in NetWasm.Compiler.Core: every runtime feature the
// compiler may record, in ordinal order. runtime-feature-ids.test.mjs fails on drift.
export const runtimeFeatureIds = Object.freeze([
  "array-set-value",
  "ephemeron-handles",
  "finalization",
  "local-time",
  "structured-command-diagnostics",
  "weak-handles",
]);
