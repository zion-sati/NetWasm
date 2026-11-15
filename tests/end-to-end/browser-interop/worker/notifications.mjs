// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export function createWorkerImports(notify) {
  return Object.freeze({ report(value) { notify("progress", [value]); } });
}
