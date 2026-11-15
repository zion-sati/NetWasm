// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum StaticInitializationHoistingPolicy
{
    Disabled,
    // A BeforeFieldInit type a method accesses on every path to a return is
    // checked once, at the method's entry.
    MethodEntry,
}
