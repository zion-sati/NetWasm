// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum PureCallReusePolicy
{
    Disabled,
    // A pure Math or MathF call repeated on the same arguments, with nothing
    // between that changes them, reuses the first call's result.
    RepeatedArguments,
}
