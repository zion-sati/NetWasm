// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export class RawModuleInspectionError extends Error {
  constructor(code, options) {
    super(`Raw module inspection failed: ${code}.`, options);
    this.name = "RawModuleInspectionError";
    this.code = code;
  }
}
