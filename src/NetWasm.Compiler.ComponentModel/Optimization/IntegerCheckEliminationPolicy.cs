// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum IntegerCheckEliminationPolicy
{
    Disabled,
    // Division, checked arithmetic and checked conversion checks that the operands' ranges rule out are not emitted.
    ProvenRanges,
}
