using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateRootObligationRecorder
{
    // Records the roots a safepoint publishes from lane addresses, and rejects storages it cannot publish exactly.
    void Record(
        int instructionOffset,
        IReadOnlyList<ManagedAggregateAddressOrigins> stack,
        IReadOnlyList<ManagedAggregateAddressOrigins> locals,
        ManagedAggregateAnalysisSession session);
}
