// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { NetWasmHostError } from "./managed-errors.mjs";

// Page code may call an export from inside a JavaScript import that an export
// called (export, import, page event, export). Every level shares one Wasm
// stack, so a bound turns runaway re-entry into a host error before it runs out.
export const maximumExportDepth = 32;

export function createExportDepthGuard(maximum = maximumExportDepth) {
  if (!Number.isSafeInteger(maximum) || maximum < 1) {
    throw new TypeError("export depth limit must be a positive integer");
  }
  let depth = 0;
  return function enterExport() {
    if (depth >= maximum) {
      throw new NetWasmHostError(
        `NetWasm export calls cannot nest more than ${maximum} levels deep`);
    }
    depth++;
    let left = false;
    return () => {
      if (left) return;
      left = true;
      depth--;
    };
  };
}
