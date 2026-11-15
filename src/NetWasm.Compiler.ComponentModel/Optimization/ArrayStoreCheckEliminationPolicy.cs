// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ArrayStoreCheckEliminationPolicy
{
    Disabled,
    // A reference store into an array whose element type is exact skips the
    // covariant store check: a sealed element class, or an array from newarr.
    ExactElementTypes,
}
