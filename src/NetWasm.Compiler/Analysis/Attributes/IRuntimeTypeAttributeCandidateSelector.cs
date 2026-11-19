// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IRuntimeTypeAttributeCandidateSelector
{
    // The run-time types whose attributes answer the query (its Target unset), in a stable order; null when one of them
    // cannot be planned, so the query keeps its run-time implementation.
    ImmutableArray<RuntimeTypeAttributeCase>? Select(ResolvedAttributeQuery query, IEnumerable<CliTypeIdentity> runtimeTypes);
}
