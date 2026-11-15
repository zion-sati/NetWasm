// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IBranchReachabilityOverlapClassifier
{
    bool Classify(ControlFlowStructuringState state, int first, int second, int? stop, ImmutableHashSet<int> allowed);
}
