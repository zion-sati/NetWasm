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
    public OptionalFunctionIndex ManagedThrowFunctionIndex { get; init; } =
        OptionalFunctionIndex.Missing;

    public OptionalFunctionIndex ManagedRethrowFunctionIndex { get; init; } =
        OptionalFunctionIndex.Missing;

    // Each delegate type's multicast function, which its invoke function calls for a
    // delegate with an invocation list.
    public ImmutableDictionary<string, WasmFunctionIndex> DelegateMulticastHelpers { get; init; } =
        ImmutableDictionary<string, WasmFunctionIndex>.Empty;

    // Managed-ABI forwarders for JSImport methods that a delegate binds directly.
    public ImmutableDictionary<EntityKey, WasmFunctionIndex> JSImportForwarders { get; init; } =
        ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty;

    public bool TryGetMethod(EntityKey method, out WasmFunctionIndex index) =>
        DirectMethods.TryGetValue(method, out index) ||
        JSImportForwarders.TryGetValue(method, out index) ||
        ImportedMethods.TryGetValue(method, out index);

    public bool TryGetInteropImport(EntityKey method, out WasmFunctionIndex index) =>
        ImportedMethods.TryGetValue(method, out index);

    public bool TryGetConstructedMethod(string method, out WasmFunctionIndex index) =>
        ConstructedMethods.TryGetValue(method, out index);

    public WasmFunctionIndex GetDelegateInvokeHelper(string delegateType) =>
        DelegateInvokeHelpers[delegateType];

    public WasmFunctionIndex GetDelegateMulticastHelper(string delegateType) =>
        DelegateMulticastHelpers[delegateType];
}
