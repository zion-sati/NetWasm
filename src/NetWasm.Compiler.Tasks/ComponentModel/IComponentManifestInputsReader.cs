using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IComponentManifestInputsReader
{
    ComponentManifestInputs Read(ComponentManifestInputsRequest request);
}

internal sealed record ComponentManifestInputsRequest(
    string InteropManifestPath,
    string Target,
    string? JcoVersion,
    string? Preview2ShimVersion);
