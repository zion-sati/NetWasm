using System.Collections.Generic;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateStorageFinder
{
    // The address-taken arguments and locals whose values could live in lanes.
    Dictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Find(ManagedAggregateValueAnalysisRequest request);
}
