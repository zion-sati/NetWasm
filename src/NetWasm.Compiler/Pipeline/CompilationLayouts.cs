// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationLayouts(ManagedLayoutSnapshot snapshot)
{
    public ManagedLayoutSnapshot Snapshot { get; } = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));
}
