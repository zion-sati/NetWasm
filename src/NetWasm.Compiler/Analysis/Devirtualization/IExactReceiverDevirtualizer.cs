// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Devirtualization;

internal interface IExactReceiverDevirtualizer
{
    // Known entry stacks may be empty; they are computed only when needed.
    CilMethodBody Rewrite(
        CilMethodBody body,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> knownStacks);
}
