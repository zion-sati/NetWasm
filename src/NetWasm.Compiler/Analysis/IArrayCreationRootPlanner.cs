// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IArrayCreationRootPlanner
{
    RuntimeIntrinsicTypeRootPlan Plan(ImmutableArray<CliTypeIdentity> elementSeeds);

    RuntimeIntrinsicTypeRootPlan Plan(
        ImmutableArray<CliTypeIdentity> elementSeeds,
        ImmutableArray<int> ranks);
}
