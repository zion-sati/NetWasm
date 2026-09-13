using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class ComponentManifestInputsReader(
    IHostInteropManifestReader manifests) : IComponentManifestInputsReader
{
    private readonly IHostInteropManifestReader _manifests = manifests ??
        throw new ArgumentNullException(nameof(manifests));

    public ComponentManifestInputs Read(ComponentManifestInputsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var interop = _manifests.Read(request.InteropManifestPath, request.Target);

        return new(
            new(
                [.. interop.Imports.Select(static import => new ComponentJavaScriptImport(
                    import.Module,
                    import.Name,
                    import.Parameters,
                    import.Result,
                    import.AsyncReturn))],
                [.. interop.Exports.Select(static export => new ComponentJavaScriptExport(
                    export.Name,
                    export.Parameters,
                    export.Result,
                    export.AsyncReturn))]),
            new(request.JcoVersion, request.Preview2ShimVersion))
        {
            WitImports = interop.WitImports.IsDefault ? [] : interop.WitImports,
        };
    }
}
