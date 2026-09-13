using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal readonly record struct WasmFunctionIndex(int Value);

internal readonly record struct OptionalFunctionIndex(WasmFunctionIndex? Index)
{
    public static OptionalFunctionIndex Missing => new(null);

    public bool IsPresent => Index.HasValue;

    public int Value => Index?.Value ?? throw new InvalidOperationException(
        "optional Wasm function is not present in module plan");

    public static OptionalFunctionIndex At(int index) => new(new(index));
}

internal sealed record FunctionIndexMap(
    ImmutableDictionary<EntityKey, WasmFunctionIndex> DirectMethods,
    ImmutableDictionary<string, WasmFunctionIndex> ConstructedMethods,
    ImmutableDictionary<string, WasmFunctionIndex> DelegateInvokeHelpers,
    ImmutableDictionary<EntityKey, WasmFunctionIndex> ImportedMethods)
{
    public bool TryGetMethod(EntityKey method, out WasmFunctionIndex index) =>
        DirectMethods.TryGetValue(method, out index) ||
        ImportedMethods.TryGetValue(method, out index);

    public bool TryGetConstructedMethod(string method, out WasmFunctionIndex index) =>
        ConstructedMethods.TryGetValue(method, out index);

    public WasmFunctionIndex GetDelegateInvokeHelper(string delegateType) =>
        DelegateInvokeHelpers[delegateType];
}
