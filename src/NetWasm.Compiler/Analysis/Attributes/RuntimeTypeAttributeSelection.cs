// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis.Attributes;

// The run-time types whose attributes answer a query on a Type known only at run time, or why one of them cannot be
// planned (an attribute that cannot be built here), in which case the query keeps its run-time implementation.
internal sealed record RuntimeTypeAttributeSelection(ImmutableArray<RuntimeTypeAttributeCase> Cases, string? Failure)
{
    public static RuntimeTypeAttributeSelection Unplannable(string failure) => new([], failure);
}
