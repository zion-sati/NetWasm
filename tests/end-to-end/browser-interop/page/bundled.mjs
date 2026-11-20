// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export function stamp(value) {
  return `bundled:${value}`;
}

// An import named "Format.wrap" finds wrap on Format and calls it with Format as `this`, as .NET does.
export const Format = Object.freeze({
  prefix: "fmt:",
  wrap(value) {
    return this.prefix + value;
  },
});
