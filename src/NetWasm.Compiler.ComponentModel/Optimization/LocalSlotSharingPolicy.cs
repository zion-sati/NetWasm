// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LocalSlotSharingPolicy
{
    Disabled,
    // Locals of one type that never hold a value from one block into another, and never appear in the same block,
    // share one local, as Release IL reuses its temporaries.
    BlockLocals,
}
