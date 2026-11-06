using System;
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
}

/// <summary>
/// The stack-trace numbering a method's emission looks up: its id, and the symbol each
/// instruction's source location resolves to.
/// </summary>
internal sealed record StackTraceMethodLookups(
    IEntryLookup<EntityKey, int> DirectMethodIds,
    IEntryLookup<string, int> ConstructedMethodIds,
    IEntryLookup<int, ImmutableArray<StackTraceLocationSymbol>> LocationSymbols)
{
    public static StackTraceMethodLookups None { get; } = Of(StackTraceMethodPlan.Disabled);

    public static StackTraceMethodLookups Of(StackTraceMethodPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(Lookups.Of(plan.DirectMethodIds), Lookups.Of(plan.ConstructedMethodIds), Lookups.Of(plan.LocationSymbols));
    }

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
