// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum UnwrittenStaticFieldFoldingPolicy
{
    Disabled,
    // A static field no reachable code stores to or takes the address of reads as its default value.
    WholeProgram,
}
