using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeNativeValidationProfile(
    int Version,
    ImmutableArray<string> Features,
    ImmutableArray<RuntimeNativeImportContract> Imports)
{
    // A plain core module's runtime imports nothing from WASI or the Component Model: its
    // contract keeps only the runtime's own imports, so any WASI import is undeclared.
    public RuntimeNativeValidationProfile WithoutWasi() => this with
    {
        Imports = [.. Imports.Where(import => !IsWasiOrComponentModel(import.Module))],
    };

    private static bool IsWasiOrComponentModel(string module) =>
        module == "$root" || module.StartsWith("wasi:", System.StringComparison.Ordinal) ||
        module.Contains("|wasi:", System.StringComparison.Ordinal);
}

internal sealed record RuntimeNativeImportContract(
    string Module,
    string Name,
    ImmutableArray<byte> Parameters,
    ImmutableArray<byte> Results,
    bool Required);
