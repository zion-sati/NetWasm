using System.Collections.Generic;
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

    // One Wasm import per native symbol (module and name), which every method declaring it shares, as each
    // binds the same function on desktop .NET; in the methods' order. The planner rejects declarations of one
    // symbol whose signatures differ.
    public ImmutableArray<WasmFunctionImport> Imports { get; } =
        [.. Methods.Select(import => import.Import).DistinctBy(import => (import.Module, import.Name))];

    // Each method's import, as its position in Imports.
    public ImmutableDictionary<EntityKey, int> ImportPositions { get; } = Positions(Methods);

    private static ImmutableDictionary<EntityKey, int> Positions(ImmutableArray<NativeMethodImport> methods)
    {
        var symbols = new Dictionary<(string Module, string Name), int>();
        var positions = ImmutableDictionary.CreateBuilder<EntityKey, int>();
        foreach (var import in methods)
        {
            var symbol = (import.Import.Module, import.Import.Name);
            if (!symbols.TryGetValue(symbol, out var position))
            {
                position = symbols.Count;
                symbols.Add(symbol, position);
            }
            positions.Add(import.Method.Definition.Key, position);
        }
        return positions.ToImmutable();
    }
}
