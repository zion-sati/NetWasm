// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Adapter: a target proven only to be a Type resolves as a closed type would. The compile-time resolver checks the
// filter and the inheritance choice with a stand-in target (any closed type), and the query keeps no target.
internal sealed class RuntimeTypeAttributeQueryResolver(IAttributeQueryResolver resolver, CliTypeIdentity closedStandIn) :
    IRuntimeTypeAttributeQueryResolver
{
    private readonly IAttributeQueryResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly CliTypeIdentity _standIn = closedStandIn ?? throw new ArgumentNullException(nameof(closedStandIn));

    public ResolvedAttributeQuery? ResolveRuntimeType(AttributeQueryCall call, ImmutableArray<AttributeValueProof> stack)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (stack.IsDefault || stack.Length < call.ArgumentCount)
        {
            throw new ArgumentException("Query arguments must have validated stack entries.", nameof(stack));
        }
        var start = stack.Length - call.ArgumentCount;
        if (stack[start].Kind != AttributeValueKind.RuntimeType)
        {
            return null;
        }
        return _resolver.Resolve(call, stack.SetItem(start, new(AttributeValueKind.Type, _standIn))) is { } query
            ? query with { Target = null }
            : null;
    }
}
