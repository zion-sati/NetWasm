using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record NativeCallbackMethodPlan(
    MethodInstanceModel Method,
    NativeAbiSignaturePlan Abi,
    string NativeSymbol,
    string RuntimeImportSymbol,
    string ThunkExportName,
    string? GetterName,
    WasmFunctionIndex? GetterIndex)
{
    public bool IsAddressTaken => GetterIndex is not null;
    public bool IsNamed => Method.Definition.NativeCallback!.EntryPoint is not null;
}

internal sealed record NativeCallbackPlan(
    ImmutableArray<NativeCallbackMethodPlan> Methods)
{
    public static NativeCallbackPlan Empty { get; } = new([]);

    public ImmutableDictionary<string, NativeCallbackMethodPlan> ByMethodIdentity { get; } =
        Methods.ToImmutableDictionary(
            method => method.Method.CanonicalName,
            System.StringComparer.Ordinal);

    public ImmutableArray<NativeCallbackMethodPlan> AddressedMethods { get; } =
        [.. Methods.Where(method => method.IsAddressTaken)];
}
