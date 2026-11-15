// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal sealed class CompilationAnalysis(ReachableProgram program)
{
    public ReachableProgram Program { get; } = program ??
        throw new ArgumentNullException(nameof(program));
}
