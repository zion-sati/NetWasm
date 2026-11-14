using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateRootObligationRecorder : IManagedAggregateRootObligationRecorder
{
    public void Record(
        int instructionOffset,
        IReadOnlyList<ManagedAggregateAddressOrigins> stack,
        IReadOnlyList<ManagedAggregateAddressOrigins> locals,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.Request.RootMap.Safepoints.TryGetValue(instructionOffset, out var safepoint))
        {
            return;
        }
        Record(safepoint.Roots, ManagedAggregateRootSetKind.Ordinary);
        Record(safepoint.ConstructorCallRoots, ManagedAggregateRootSetKind.Constructor);

        void Record(
            ImmutableArray<RootSource> sources,
            ManagedAggregateRootSetKind set)
        {
            foreach (var source in sources)
            {
                var origins = source.Kind switch
                {
                    RootSourceKind.ManagedAddressLocal
                        when (uint)source.Index < (uint)locals.Count => locals[source.Index],
                    RootSourceKind.ManagedAddressEvaluationStack
                        when (uint)source.Index < (uint)stack.Count => stack[source.Index],
                    RootSourceKind.EvaluationStack
                        when source.IsIndirect &&
                            (uint)source.Index < (uint)stack.Count => stack[source.Index],
                    _ => ManagedAggregateAddressOrigins.None,
                };
                if (origins.Recipes.IsEmpty)
                {
                    continue;
                }
                if (source.IsIndirect || origins.Unknown || origins.Recipes.Count != 1)
                {
                    session.Reject(origins);
                    continue;
                }
                session.RootDependencies.Add(new(
                    instructionOffset,
                    source,
                    origins.Recipes.Single(),
                    set));
            }
        }
    }
}
