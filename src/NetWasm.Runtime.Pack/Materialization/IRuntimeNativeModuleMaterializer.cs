using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeModuleMaterializer
{
    RuntimeMaterialization Materialize(RuntimeNativeMaterializationRequest request);
}

internal sealed record RuntimeNativeMaterializationRequest(
    RuntimeMaterializationRequest Consumer,
    RuntimePackManifest Manifest,
    RuntimePackTarget Target,
    RuntimeLayout SourceLayout,
    ImmutableArray<string> SystemLibraryPaths);
