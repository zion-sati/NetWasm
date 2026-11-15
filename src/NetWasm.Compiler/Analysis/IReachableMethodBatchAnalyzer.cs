// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Collections.Generic;

namespace NetWasm.Compiler.Analysis;

internal interface IReachableMethodBatchAnalyzer
{
    IEnumerable<ReachableMethodAnalysis> Analyze(
        ImmutableArray<ReachableMethodRequest> requests);
}
