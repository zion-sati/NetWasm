// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

// What becomes of an attribute query whose target is a Type known only at run time: answered by a switch over the
// program's run-time types (RuntimeTypeAttributeQueryReducer), or kept at its run-time path, which throws
// PlatformNotSupportedException, and why (UnplannedAttributeQueryFinder reports it).
internal abstract record RuntimeTypeAttributeQueryDecision
{
    private RuntimeTypeAttributeQueryDecision()
    {
    }

    // The target is not a Type known only at run time: the query is not this lowering's.
    internal sealed record NotRunTimeType : RuntimeTypeAttributeQueryDecision;

    internal sealed record Answered(ResolvedAttributeQuery Query, ImmutableArray<RuntimeTypeAttributeCase> Cases)
        : RuntimeTypeAttributeQueryDecision;

    // The filter is not known at compile time, or takes every attribute of every candidate type (object, Attribute, an
    // interface), whose construction the switch would root.
    internal sealed record Unresolved : RuntimeTypeAttributeQueryDecision;

    // An attribute the filter selects on one of the program's run-time types cannot be built here.
    internal sealed record Unbuildable(string Reason) : RuntimeTypeAttributeQueryDecision;
}
