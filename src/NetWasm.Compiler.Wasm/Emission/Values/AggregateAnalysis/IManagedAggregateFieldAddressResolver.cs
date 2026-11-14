using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateFieldAddressResolver
{
    // Where the address of a field (ldflda) of a value at the given origins points.
    ManagedAggregateAddressOrigins Resolve(
        CilInstruction instruction,
        ManagedAggregateAddressOrigins origins,
        ManagedAggregateAnalysisSession session);
}
