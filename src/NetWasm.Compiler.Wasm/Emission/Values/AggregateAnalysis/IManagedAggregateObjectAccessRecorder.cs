using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateObjectAccessRecorder
{
    // Records a primitive load or store through an address (ldobj, stobj) at the given origins as a lane access.
    void Record(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressOrigins origins,
        bool writes,
        ManagedAggregateAnalysisSession session);
}
