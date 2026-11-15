// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopInvariantFieldHoistingPolicy
{
    Disabled,
    Conservative,
    // Conservative, plus a loop that checks an invariant array for null
    // before any other effect makes that check once on entry.
    NullChecks,
}
