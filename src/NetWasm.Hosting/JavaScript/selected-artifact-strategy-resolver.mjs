// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

export function createSelectedArtifactStrategyResolver(expectedKind, execute) {
  return kind => {
    if (kind !== expectedKind) throw new TypeError("deployment kind is unsupported");
    return execute;
  };
}
