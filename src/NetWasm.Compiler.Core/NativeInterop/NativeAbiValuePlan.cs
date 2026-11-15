// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core.NativeInterop;

public sealed record NativeAbiValuePlan(
    NativeAbiValueKind Kind,
    CliTypeIdentity LogicalType,
    CliTypeIdentity? PhysicalType,
    int Size = 0,
    int Alignment = 1,
    CliTypeIdentity? ScalarStorageType = null,
    int ScalarOffset = 0);
