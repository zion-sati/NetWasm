// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ConditionalStoreMergingPolicy
{
    Disabled,
    // Both arms of an if storing one field compute their values and store once where they meet.
    SameFieldArms,
}
