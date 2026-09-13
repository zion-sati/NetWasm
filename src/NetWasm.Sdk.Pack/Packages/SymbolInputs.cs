using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Packages;

public sealed record SymbolInputs(
    bool IncludeSymbols,
    string Format,
    ImmutableArray<SymbolInput> Files)
{
    public static SymbolInputs None { get; } = new(false, "snupkg", ImmutableArray<SymbolInput>.Empty);
}
