// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export function register(callback) {
  globalThis.reproCallback = callback;
  globalThis.reproReady?.();
  return { dispose() { globalThis.reproCallback = null; } };
}
