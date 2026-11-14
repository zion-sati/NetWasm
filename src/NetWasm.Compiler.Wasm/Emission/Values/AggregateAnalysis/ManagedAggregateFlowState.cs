using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// The address origins of each stack slot and local where a block starts or leaves.
internal sealed record ManagedAggregateFlowState(
    ImmutableArray<ManagedAggregateAddressOrigins> Stack,
    ImmutableArray<ManagedAggregateAddressOrigins> Locals);
