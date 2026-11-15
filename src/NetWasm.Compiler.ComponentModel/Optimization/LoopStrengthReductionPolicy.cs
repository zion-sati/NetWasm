// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopStrengthReductionPolicy
{
    Disabled,
    // A loop's reads of a reference plus its counter read a reference that moves with the counter.
    AddressInduction,
}
