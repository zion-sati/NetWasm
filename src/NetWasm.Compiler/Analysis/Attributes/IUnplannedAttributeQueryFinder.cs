// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IUnplannedAttributeQueryFinder
{
    // Every reference in the program's reached code that keeps an attribute query's run-time path (NW1019).
    ImmutableArray<UnplannedAttributeQuery> Find(ReachableProgram program);
}

// A program without System.Attribute has no attribute query to report.
internal sealed class NoUnplannedAttributeQueries : IUnplannedAttributeQueryFinder
{
    public static readonly NoUnplannedAttributeQueries Instance = new();

    public ImmutableArray<UnplannedAttributeQuery> Find(ReachableProgram program) => [];
}
