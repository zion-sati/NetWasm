// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum AddressTakenValuePromotionPolicy
{
    Disabled,
    Conservative,
    // Conservative, plus aggregates with reference and byref fields, whose
    // fields become typed lanes the root map publishes.
    References,
}
