using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Pipeline;

/// <summary>
/// The stack-trace symbols the last compile's methods take from the current sources, and the
/// locations each edited method takes, by its id. An edit may move only where in its own body
/// its locations start: any other difference (a symbol, or which symbols a method's locations
/// take) would number the symbols as a full compile would not.
/// </summary>
internal sealed record EarlyCutoffStackTraces(
    StackTraceSymbolTable Table,
    ImmutableDictionary<int, UnitLookup<ImmutableArray<StackTraceLocationSymbol>>> EditedLocations)
{
    // The symbols, or null when they are not the last compile's numbering: it kept none
    // (it had no symbols), or the sources now give other ones.
    public static EarlyCutoffStackTraces? Replay(UnitStackTraces? numbering, IMethodRepository methods,
        ISymbolFormatter symbols, ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> sourceLocations,
        IEnumerable<EntityKey> edited)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(edited);
        if (numbering is null) return null;
        var table = StackTraceSymbolTable.Build(
            [.. numbering.DirectMethodIds.OrderBy(pair => pair.Value)
                .Select(pair => (pair.Key, symbols.Format(methods.GetMethod(pair.Key))))],
            [.. numbering.ConstructedMethodIds.OrderBy(pair => pair.Value)
                .Select(pair => new StackTraceConstructedMethod(pair.Key, numbering.ConstructedDefinitions[pair.Key]))],
            sourceLocations);
        if (StackTraceSymbolTable.Fingerprint(table.Symbols, table.LocationSymbols) != numbering.Table) return null;
        var locations = ImmutableDictionary.CreateBuilder<int, UnitLookup<ImmutableArray<StackTraceLocationSymbol>>>();
        foreach (var method in edited)
        {
            if (!numbering.DirectMethodIds.TryGetValue(method, out var id)) continue;
            var found = table.LocationSymbols.TryGetValue(id, out var symbolsOf);
            locations[id] = new(found, symbolsOf);
        }
        return new(table, locations.ToImmutable());
    }
}
