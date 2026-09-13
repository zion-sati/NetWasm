using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IFilterEmissionCountMerger
{
    void Merge(
        IList<ManagedMethodEmissionRecord> emissions,
        StructuredMethod filterMethod,
        ImmutableDictionary<int, int> filterCounts);
}
