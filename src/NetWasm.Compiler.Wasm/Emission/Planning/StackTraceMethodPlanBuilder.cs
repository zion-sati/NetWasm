using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Exceptions;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class StackTraceMethodPlanBuilder(
    IMethodRepository methods,
    ISymbolFormatter symbols,
    IExceptionFieldLayoutResolver exceptionFields,
    ITypeLayoutProvider typeLayouts) : IStackTraceMethodPlanBuilder
{
    // .NET's StackTrace.ShowInStackTrace: a method marked StackTraceHidden, or
    // whose type is, and an AggressiveInlining method record no frame, so a trace
    // starts where a throw helper was called.
    private static bool Shown(MethodDefinitionModel method) =>
        !method.IsStackTraceHidden && !method.IsAggressiveInlining;

    public StackTraceMethodPlan Build(
        bool enabled,
        ImmutableArray<EntityKey> directMethods,
        ImmutableArray<StackTraceConstructedMethod> constructedMethods,
        ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>
            sourceLocations)
    {
        if (!enabled)
        {
            return StackTraceMethodPlan.Disabled;
        }

        var shownMethods = directMethods
            .Select(methods.GetMethod)
            .Where(Shown)
            .Select(method => (method.Key, symbols.Format(method)))
            .ToImmutableArray();
        var shownConstructed = constructedMethods
            .Where(method => Shown(methods.GetMethod(method.DefinitionKey)))
            .ToImmutableArray();
        var table = StackTraceSymbolTable.Build(shownMethods, shownConstructed, sourceLocations);
        var traceOffset = exceptionFields.Resolve("_stackTrace") ??
            throw new InvalidOperationException(
                "stack traces require the reachable System.Exception._stackTrace field");
        return new(table.DirectMethodIds, table.ConstructedMethodIds, table.Symbols, table.LocationSymbols,
            traceOffset, typeLayouts.StringTypeId)
        {
            ConstructedDefinitions = shownConstructed.ToImmutableDictionary(
                method => method.Name, method => method.DefinitionKey, StringComparer.Ordinal),
        };
    }
}

/// <summary>
/// A stack-trace plan's numbering: each shown method in order, then one symbol for each
/// source line its locations name, method by method. The plan builder and the early
/// cutoff, which renumbers the last compile's methods against the current sources, use it
/// alike.
/// </summary>
internal sealed record StackTraceSymbolTable(
    ImmutableDictionary<EntityKey, int> DirectMethodIds,
    ImmutableDictionary<string, int> ConstructedMethodIds,
    ImmutableArray<WasmStackTraceSymbol> Symbols,
    ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>> LocationSymbols)
{
    // Everything of a table but where in each body a location starts: its symbols and, per
    // method, which symbols its locations take. Two tables alike here differ only in the IL
    // offsets of their methods' locations, which each method's own code alone reads.
    public static Units.UnitFingerprint Fingerprint(
        ImmutableArray<WasmStackTraceSymbol> symbols,
        IReadOnlyDictionary<int, ImmutableArray<StackTraceLocationSymbol>> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var fingerprint = new Units.UnitFingerprintBuilder();
        fingerprint.Add(symbols.Length);
        foreach (var symbol in symbols)
        {
            fingerprint.Add(symbol.Id);
            fingerprint.Add(symbol.Name);
        }
        fingerprint.Add(locations.Count);
        foreach (var (method, list) in locations.OrderBy(pair => pair.Key))
        {
            fingerprint.Add(method);
            fingerprint.Add(list.Length);
            foreach (var location in list) fingerprint.Add(location.SymbolId);
        }
        return fingerprint.Finish();
    }

    public static StackTraceSymbolTable Build(
        IReadOnlyList<(EntityKey Method, string Name)> directMethods,
        IReadOnlyList<StackTraceConstructedMethod> constructedMethods,
        ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> sourceLocations)
    {
        ArgumentNullException.ThrowIfNull(directMethods);
        ArgumentNullException.ThrowIfNull(constructedMethods);
        ArgumentNullException.ThrowIfNull(sourceLocations);
        var direct = ImmutableDictionary.CreateBuilder<EntityKey, int>();
        var constructed = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var methodSymbols = ImmutableArray.CreateBuilder<WasmStackTraceSymbol>();
        var id = 1;
        foreach (var (method, name) in directMethods)
        {
            direct.Add(method, id);
            methodSymbols.Add(new(id++, name));
        }
        foreach (var method in constructedMethods)
        {
            constructed.Add(method.Name, id);
            methodSymbols.Add(new(id++, method.Name));
        }
        var locationIds = ImmutableDictionary.CreateBuilder<int, ImmutableArray<StackTraceLocationSymbol>>();
        foreach (var (method, name) in directMethods)
        {
            AddLocations(method, direct[method], name);
        }
        foreach (var method in constructedMethods)
        {
            AddLocations(method.DefinitionKey, constructed[method.Name], method.Name);
        }
        return new(direct.ToImmutable(), constructed.ToImmutable(), methodSymbols.ToImmutable(), locationIds.ToImmutable());

        void AddLocations(EntityKey definition, int methodId, string methodName)
        {
            if (!sourceLocations.TryGetValue(definition, out var locations))
            {
                return;
            }
            var byOffset = ImmutableArray.CreateBuilder<StackTraceLocationSymbol>();
            foreach (var location in locations.OrderBy(location => location.Offset))
            {
                if (location.Document is null || location.StartLine <= 0)
                {
                    byOffset.Add(new(location.Offset, methodId));
                    continue;
                }
                var symbolId = id++;
                byOffset.Add(new(location.Offset, symbolId));
                methodSymbols.Add(new(
                    symbolId,
                    $"{methodName} in {location.Document}:line {location.StartLine}"));
            }
            if (byOffset.Count != 0)
            {
                locationIds.Add(methodId, byOffset.ToImmutable());
            }
        }
    }
}
