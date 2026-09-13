using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedMethodEmissionMetricProjector :
    IManagedMethodEmissionMetricProjector
{
    public ImmutableArray<WasmManagedMethodEmissionMetric> Project(
        IEnumerable<ManagedMethodEmissionRecord> emissions)
    {
        ArgumentNullException.ThrowIfNull(emissions);
        return [.. emissions.Select(item => new WasmManagedMethodEmissionMetric(
            item.Identity,
            item.Emission.WasmInstructionCount,
            item.Emission.Body.Length,
            item.Emission.CompileDurationTicks,
            item.Emission.PeakObservedManagedMemoryBytes,
            item.Emission.OriginalBlockEmissionCounts))];
    }
}
