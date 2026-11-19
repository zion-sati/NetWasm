// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

// Whether a branch whose arms only compute a number becomes a select.
public enum ConditionalSelectPolicy
{
    Disabled,
    Enabled,
}
