// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// One provider module serves both hosts: the page binds it directly, and the
// worker binds it beside its notification channel.
export function createWorkerImports(notify) {
  return Object.freeze({
    label(value) {
      notify("labeled", [value]);
      return `#${value}`;
    },
  });
}
