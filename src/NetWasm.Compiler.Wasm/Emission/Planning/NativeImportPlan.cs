using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record NativeMethodImport(
    MethodInstanceModel Method,
    NativeAbiPlan Abi,
    WasmFunctionImport Import);

internal sealed record NativeImportPlan(ImmutableArray<NativeMethodImport> Methods)
{
    public static NativeImportPlan Empty { get; } = new([]);

    // The unmanaged calli sites' lowerings and the trampolines they call (issue 226).
    public NativeCallSitePlan CallSites { get; init; } = NativeCallSitePlan.Empty;

    public bool IsEmpty => Methods.IsEmpty && CallSites.Trampolines.IsEmpty;

    public ImmutableDictionary<EntityKey, NativeMethodImport> ByMethod { get; } =
        Methods.ToImmutableDictionary(import => import.Method.Definition.Key);
}
