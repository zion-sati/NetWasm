using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimePackManifest(
    int SchemaVersion,
    string RuntimeAbi,
    string EmscriptenVersion,
    long WasmPageSize,
    ImmutableArray<string> Exports,
    RuntimePackProvenance Provenance,
    ImmutableArray<RuntimePackTarget> Targets);

internal sealed record RuntimePackProvenance(
    string BuildSeam,
    string Configuration,
    string ToolchainFile,
    string ToolchainFingerprint,
    string RuntimeSource,
    string LayoutAuthority);

internal sealed record RuntimePackTarget(
    string Target,
    int PointerSizeBytes,
    long Alignment,
    long RuntimeFootprintBytes,
    long NativeStackSizeBytes,
    long DefaultInitialHeapSizeBytes,
    long DefaultMaximumMemorySizeBytes,
    long MaximumMemorySizeBytes,
    RuntimePackAsset RuntimeArchive,
    RuntimePackSystemLibraries SystemLibraries);

internal sealed record RuntimePackSystemLibraries(
    ImmutableArray<string> Names,
    ImmutableArray<RuntimePackAsset> Assets);

internal sealed record RuntimePackAsset(
    string Path,
    string Sha256);

internal sealed record RuntimeLayout(
    int SchemaVersion,
    string Target,
    long ApplicationStaticDataEnd);

internal sealed record RuntimeMemoryLayoutRequest(
    RuntimePackTarget Target,
    long WasmPageSize,
    long ApplicationStaticDataEnd,
    long? InitialHeapSizeBytes,
    long? MaximumMemorySizeBytes);

internal sealed record RuntimeMemoryLayout(
    long RuntimeGlobalBase,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes);

internal sealed record RuntimeLinkRequest(
    RuntimePackManifest Manifest,
    RuntimePackTarget Target,
    RuntimeMemoryLayout Layout,
    string AssetRoot,
    ImmutableArray<string> SystemLibraryPaths,
    string OutputPath);

internal sealed record RuntimeCommand(
    string ExecutablePath,
    ImmutableArray<string> Arguments,
    string LogPath);

internal sealed record RuntimeMaterializationRequest(
    string ManifestPath,
    string RuntimeLayoutPath,
    string AssetRoot,
    string WasmLdPath,
    string WasmToolsNodePath,
    string WasmToolsCommandPath,
    string WasmToolsModulePath,
    string OutputPath,
    string LogDirectory,
    string Target,
    long? InitialHeapSizeBytes,
    long? MaximumMemorySizeBytes);

internal sealed record RuntimeMaterialization(
    string Target,
    string OutputPath,
    string Sha256,
    string RuntimeAbi,
    string BuildSeam,
    string ToolchainFingerprint,
    long RuntimeGlobalBase,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes);
