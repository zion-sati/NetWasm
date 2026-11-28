// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: the one decision on a query whose target is a Type known only at run time, which the reducer acts on and the
// final program's report explains, so the two never disagree.
internal sealed class RuntimeTypeAttributeQueryAcceptor(
    IRuntimeTypeAttributeQueryResolver resolver,
    IRuntimeTypeAttributeCandidateSelector candidates) : IRuntimeTypeAttributeQueryAcceptor
{
    private readonly IRuntimeTypeAttributeQueryResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly IRuntimeTypeAttributeCandidateSelector _candidates = candidates ??
        throw new ArgumentNullException(nameof(candidates));

    public RuntimeTypeAttributeQueryDecision Decide(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack,
        IEnumerable<CliTypeIdentity> runtimeTypes)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(runtimeTypes);
        if (stack.IsDefault || stack.Length < call.ArgumentCount)
        {
            throw new ArgumentException("Query arguments must have validated stack entries.", nameof(stack));
        }
        if (stack[stack.Length - call.ArgumentCount].Kind != AttributeValueKind.RuntimeType)
        {
            return new RuntimeTypeAttributeQueryDecision.NotRunTimeType();
        }
        if (_resolver.ResolveRuntimeType(call, stack) is not { } query)
        {
            return new RuntimeTypeAttributeQueryDecision.Unresolved();
        }
        var selection = _candidates.Select(query, runtimeTypes);
        return selection.Failure is { } failure
            ? new RuntimeTypeAttributeQueryDecision.Unbuildable(failure)
            : new RuntimeTypeAttributeQueryDecision.Answered(query, selection.Cases);
    }
}
