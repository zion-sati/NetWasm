using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record StackTraceMethodPlan(
    ImmutableDictionary<EntityKey, int> DirectMethodIds,
    ImmutableDictionary<string, int> ConstructedMethodIds,
    ImmutableArray<WasmStackTraceSymbol> Symbols,
    int ExceptionTraceOffset,
    int StringTypeId)
{
    public static StackTraceMethodPlan Disabled { get; } = new(
        ImmutableDictionary<EntityKey, int>.Empty,
        ImmutableDictionary<string, int>.Empty,
        [],
        0,
        0);
}
