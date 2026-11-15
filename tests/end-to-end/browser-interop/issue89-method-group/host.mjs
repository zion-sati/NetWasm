// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

export function resolve(name) { return `value of ${name}`; }
export function report(value) { globalThis.reproReported = value; }
