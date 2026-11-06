using System;
using System.Collections.Frozen;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// The module's function index space by symbol: a name for each function that stays the
/// same across compiles while the function exists, so a unit's call can be rebound to
/// the function's index in another module. Imports are named by module and field;
/// managed methods by canonical name, never by metadata token, which adding a method to
/// an assembly shifts. Functions without a stable name, and any index two symbols would
/// share, are left out; a body that calls one is not reused.
/// </summary>
internal sealed record WasmFunctionSymbols(
    FrozenDictionary<uint, string> ByIndex,
    FrozenDictionary<string, uint> BySymbol)
{
    public static WasmFunctionSymbols Empty { get; } = new(
        FrozenDictionary<uint, string>.Empty,
        FrozenDictionary.ToFrozenDictionary<string, uint>([], StringComparer.Ordinal));

    // Symbols are compared ordinally, so a span of one looks it up without a copy.
    public FrozenDictionary<string, uint>.AlternateLookup<ReadOnlySpan<char>> BySpan =>
        BySymbol.GetAlternateLookup<ReadOnlySpan<char>>();
}
