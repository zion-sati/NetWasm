using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// Each whole-value store, and each call that writes the storage, flushes
// and reloads every lane, so lanes pay off only where they are accessed in
// a loop the stores sit outside of, as an enumerator is assigned before the
// loop that moves it. A value copied into the storage on every iteration
// stays in memory, and so does an enumerator whose MoveNext writes it on
// every iteration: a reload of every lane after each call costs more than
// the loop's one read of Current from memory.
internal sealed class UnamortizedAggregateStoreRejector(
    IStructuredLoopDepthMeasurer loopDepths) : IUnamortizedAggregateStoreRejector
{
    private readonly IStructuredLoopDepthMeasurer _loopDepths = loopDepths ??
        throw new ArgumentNullException(nameof(loopDepths));

    public void Reject(ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.WholeValueStores.Count == 0)
        {
            return;
        }
        var depths = _loopDepths.Measure(session.Request.Method.Body);
        foreach (var stores in session.WholeValueStores.GroupBy(store => store.Storage))
        {
            var deepestAccess = session.Accesses.Values
                .Where(access => access.Address.Storage == stores.Key)
                .Select(access => depths.GetValueOrDefault(access.Block))
                .DefaultIfEmpty(-1)
                .Max();
            if (stores.Any(store => depths.GetValueOrDefault(store.Block) >= deepestAccess))
            {
                session.Reject(stores.Key);
            }
        }
    }
}
