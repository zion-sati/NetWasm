// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// A zero-delay timer: its callback runs only once C# has returned control to the browser.
let ran = false;

export function arm() {
  ran = false;
  setTimeout(() => { ran = true; }, 0);
}

export function timerRan() {
  return ran;
}
