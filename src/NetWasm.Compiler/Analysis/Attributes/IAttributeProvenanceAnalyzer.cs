// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.Analysis.Attributes;

internal interface IAttributeProvenanceAnalyzer
{
    ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze(ValidatedControlFlowGraph controlFlow);
}
