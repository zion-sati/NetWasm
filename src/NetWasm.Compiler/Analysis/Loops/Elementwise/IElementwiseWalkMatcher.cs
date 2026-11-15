// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal interface IElementwiseWalkMatcher
{
    // The walk the vector loop can take, if its body only folds and copies the element its reference points to.
    ElementwiseWalk? Match(CountedWalk counted, ImmutableArray<CliValueKind> locals);
}
