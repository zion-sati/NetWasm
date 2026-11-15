// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum DispatcherEliminationPolicy
{
    Disabled,
    // A method without exception regions whose control flow is reducible is
    // structured with labeled blocks, loops and branches where structuring
    // would otherwise route its transitions through a dispatcher.
    Reducible,
}
