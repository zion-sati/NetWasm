// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ReachableProgramReductionChain(IEnumerable<IReachableProgramReducer> reducers) : IReachableProgramReducer
{
    private readonly ImmutableArray<IReachableProgramReducer> _reducers = reducers is null
        ? throw new ArgumentNullException(nameof(reducers))
        : [.. reducers.Select(reducer => reducer ?? throw new ArgumentException("A reducer is required.", nameof(reducers)))];

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        foreach (var reducer in _reducers)
        {
            var changes = reducer.Reduce(program, roots);
            if (!changes.IsEmpty) return changes;
        }
        return ImmutableDictionary<string, CilMethodBody>.Empty;
    }
}
