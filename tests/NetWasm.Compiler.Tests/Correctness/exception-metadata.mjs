// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export var exceptionClauseSize = 12;
export var exceptionClauseValueOffset = 4;

export function readExceptionClause(view, metadata, index, plus, asNumber) {
  var address = plus(metadata, index * exceptionClauseSize);
  return {
    kind: view.getUint32(asNumber(address), true),
    value: view.getUint32(
      asNumber(plus(address, exceptionClauseValueOffset)),
      true),
  };
}
