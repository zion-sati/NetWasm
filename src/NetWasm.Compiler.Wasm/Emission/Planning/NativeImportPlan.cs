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

    public ImmutableDictionary<EntityKey, NativeMethodImport> ByMethod { get; } =
        Methods.ToImmutableDictionary(import => import.Method.Definition.Key);
}
