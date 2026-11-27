// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IPseudoAttributeSelector
{
    // The attributes desktop .NET adds to a type's own, from its flags rather than its metadata, that the filter
    // takes: each constructed by its parameterless constructor.
    ImmutableArray<AttributeConstructionPlan> Select(CliTypeIdentity target, CliTypeIdentity filter);
}
