using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// Where a managed address on the stack or in a local may point: the candidate storages it may address, and whether it
// may also address anything else.
internal readonly record struct ManagedAggregateAddressOrigins(
    bool Unknown,
    ImmutableHashSet<ManagedAggregateAddressRecipe> Recipes)
{
    public static ManagedAggregateAddressOrigins None { get; } = new(false, []);
    public static ManagedAggregateAddressOrigins UnknownOnly { get; } = new(true, []);

    public ManagedAggregateAddressOrigins Union(ManagedAggregateAddressOrigins other) => new(
        Unknown || other.Unknown,
        Recipes.Union(other.Recipes));
}
