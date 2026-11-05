using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal readonly record struct StackTraceLocationSymbol(int Offset, int SymbolId);

internal readonly record struct StackTraceConstructedMethod(
    string Name,
    EntityKey DefinitionKey);

internal sealed record StackTraceMethodPlan(
    IReadOnlyDictionary<EntityKey, int> DirectMethodIds,
    IReadOnlyDictionary<string, int> ConstructedMethodIds,
    ImmutableArray<WasmStackTraceSymbol> Symbols,
    IReadOnlyDictionary<int, ImmutableArray<StackTraceLocationSymbol>> LocationSymbols,
    int ExceptionTraceOffset,
    int StringTypeId)
{
    // Each shown constructed method's definition, whose source locations it takes.
    public IReadOnlyDictionary<string, EntityKey> ConstructedDefinitions { get; init; } =
        ImmutableDictionary<string, EntityKey>.Empty;

    public static StackTraceMethodPlan Disabled { get; } = new(
        ImmutableDictionary<EntityKey, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        [],
        ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>>.Empty,
        0,
        0);

    public int ResolveSymbolId(int methodId, int instructionOffset)
    {
        if (!LocationSymbols.TryGetValue(methodId, out var locations) ||
            locations.IsEmpty)
        {
            return methodId;
        }

        var lower = 0;
        var upper = locations.Length - 1;
        var resolved = methodId;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var candidate = locations[middle];
            if (candidate.Offset > instructionOffset)
            {
                upper = middle - 1;
            }
            else
            {
                resolved = candidate.SymbolId;
                lower = middle + 1;
            }
        }
        return resolved;
    }
}
