// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRuntimeIntrinsicTypeRootPlanner
{
    RuntimeIntrinsicTypeRootPlan Plan(
        IEnumerable<MethodInstanceModel> reachableMethods,
        IEnumerable<CliTypeIdentity> reachableTypes,
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> arrayElementSeeds = default,
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> typeHandleSeeds = default,
        System.Collections.Immutable.ImmutableArray<CliTypeIdentity> programAllocatedTypes = default);
}
