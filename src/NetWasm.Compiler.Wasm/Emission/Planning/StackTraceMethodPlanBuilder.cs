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

        var direct = ImmutableDictionary.CreateBuilder<EntityKey, int>();
        var constructed = ImmutableDictionary.CreateBuilder<string, int>(
            StringComparer.Ordinal);
        var methodSymbols = ImmutableArray.CreateBuilder<WasmStackTraceSymbol>();
        var methodNames = new Dictionary<EntityKey, string>();
        var id = 1;
        foreach (var method in directMethods)
        {
            direct.Add(method, id);
            var name = symbols.Format(methods.GetMethod(method));
            methodNames.Add(method, name);
            methodSymbols.Add(new(id++, name));
        }
        foreach (var method in constructedMethods)
        {
            constructed.Add(method.Name, id);
            methodSymbols.Add(new(id++, method.Name));
        }
        var locationIds = ImmutableDictionary.CreateBuilder<
            int,
            ImmutableArray<StackTraceLocationSymbol>>();
        foreach (var method in directMethods)
        {
            AddLocations(method, direct[method], methodNames[method]);
        }
        foreach (var method in constructedMethods)
        {
            AddLocations(method.DefinitionKey, constructed[method.Name], method.Name);
        }
        var traceOffset = exceptionFields.Resolve("_stackTrace") ??
            throw new InvalidOperationException(
                "stack traces require the reachable System.Exception._stackTrace field");
        return new(direct.ToImmutable(), constructed.ToImmutable(),
            methodSymbols.ToImmutable(), locationIds.ToImmutable(), traceOffset,
            typeLayouts.StringTypeId);

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
