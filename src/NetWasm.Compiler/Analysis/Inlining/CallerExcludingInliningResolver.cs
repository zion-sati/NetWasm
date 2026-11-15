// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

// Inlines nothing into the excluded methods, whose functions came out over an engine limit in an earlier compile
// of the program, and leaves every other caller's inlining to the resolver it decorates.
internal sealed class CallerExcludingInliningResolver(
    IManagedInliningResolver resolver,
    ImmutableHashSet<EntityKey> excludedCallers) : IManagedInliningResolver
{
    private readonly IManagedInliningResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    private readonly ImmutableHashSet<EntityKey> _excludedCallers = excludedCallers ??
        throw new ArgumentNullException(nameof(excludedCallers));

    public ManagedInliningPlan? Resolve(ManagedInliningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _excludedCallers.Contains(request.Caller.Definition.Key) ? null : _resolver.Resolve(request);
    }
}
