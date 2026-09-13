using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IManagedMethodEmissionMetricProjector
{
    ImmutableArray<WasmManagedMethodEmissionMetric> Project(
        IEnumerable<ManagedMethodEmissionRecord> emissions);
}
