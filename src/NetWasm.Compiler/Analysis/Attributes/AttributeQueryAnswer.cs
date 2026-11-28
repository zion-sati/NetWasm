// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// A query's compile-time answer at its call: the CIL that answers it, or why there is none.
internal abstract record AttributeQueryAnswer
{
    private AttributeQueryAnswer()
    {
    }

    internal sealed record Answered(AttributeCilFragment Fragment) : AttributeQueryAnswer;

    // The call's proofs name no one closed target and filter.
    internal sealed record Unresolved : AttributeQueryAnswer;

    // An attribute the query selects cannot be built here; a broad filter may select attributes the program never names.
    internal sealed record Unbuildable(CompilerException Exception, bool Broad) : AttributeQueryAnswer;
}
