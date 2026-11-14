using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateInstructionTransfer
{
    // Applies one instruction to the address origins on the stack and in locals, recording what it does to the
    // candidate storages and rejecting those it lets escape.
    void Apply(
        ManagedAggregateInstructionStep step,
        List<ManagedAggregateAddressOrigins> stack,
        ImmutableArray<ManagedAggregateAddressOrigins>.Builder locals,
        ManagedAggregateAnalysisSession session);
}
