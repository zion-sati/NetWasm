using System;
using System.Collections.Immutable;
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
        ImmutableArray<string> constructedMethods)
    {
        if (!enabled)
        {
            return StackTraceMethodPlan.Disabled;
        }

        var direct = ImmutableDictionary.CreateBuilder<EntityKey, int>();
        var constructed = ImmutableDictionary.CreateBuilder<string, int>(
            StringComparer.Ordinal);
        var methodSymbols = ImmutableArray.CreateBuilder<WasmStackTraceSymbol>();
        var id = 1;
        foreach (var method in directMethods)
        {
            direct.Add(method, id);
            methodSymbols.Add(new(id++, symbols.Format(methods.GetMethod(method))));
        }
        foreach (var method in constructedMethods)
        {
            constructed.Add(method, id);
            methodSymbols.Add(new(id++, method));
        }
        var traceOffset = exceptionFields.Resolve("_stackTrace") ??
            throw new InvalidOperationException(
                "stack traces require the reachable System.Exception._stackTrace field");
        return new(direct.ToImmutable(), constructed.ToImmutable(),
            methodSymbols.ToImmutable(), traceOffset, typeLayouts.StringTypeId);
    }
}
