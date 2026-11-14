using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateBlockTransfer
{
    // Applies a block's instructions and exit to its entry state; returns the state its successors receive.
    ManagedAggregateFlowState Apply(
        StructuredBlockId block,
        ManagedAggregateFlowState entry,
        ManagedAggregateAnalysisSession session);
}
