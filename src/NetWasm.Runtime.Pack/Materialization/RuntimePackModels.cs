using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Planning;

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
    RuntimePackAsset CollectorArchive,
    RuntimePackAsset AllowedUndefinedSymbols,
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

internal sealed record RuntimeOptimizationRequest(
    RuntimePackTarget Target,
    string OutputPath,
    RuntimeWasmOptimization Optimization);

internal sealed record RuntimeCommand(
    string ExecutablePath,
    ImmutableArray<string> Arguments,
    string LogPath);

internal sealed record RuntimeBuildIdentity(
    string SdkVersion,
    string CompilerVersion,
    string RuntimeVersion,
    string RuntimePackVersion,
    string HostToolsPackageId,
    string HostToolsPackageVersion,
    string WasmLdVersion,
    string WasmOptVersion,
    string WasmToolsVersion,
    string NodeVersion);

internal sealed record RuntimeMaterializationRequest(
    string ManifestPath,
    string RuntimeLayoutPath,
    string AssetRoot,
    string WasmLdPath,
    string WasmOptPath,
    string WasmToolsNodePath,
    string WasmToolsCommandPath,
    string WasmToolsModulePath,
    string OutputPath,
    string LogDirectory,
    string CacheDirectory,
    string Target,
    RuntimeWasmOptimization Optimization,
    long? InitialHeapSizeBytes,
    long? MaximumMemorySizeBytes,
    RuntimeBuildIdentity BuildIdentity);

internal sealed record RuntimeMaterializationCacheKeyRequest(
    RuntimeBuildIdentity BuildIdentity,
    RuntimePackManifest Manifest,
    RuntimePackTarget Target,
    RuntimeMemoryLayout Layout,
    RuntimeWasmOptimization Optimization,
    ImmutableArray<string> LinkArguments,
    ImmutableArray<string> OptimizationArguments,
    string AssetRoot,
    string OutputPath);

internal sealed record RuntimeMaterializationCacheKey(string Value)
{
    public string Prefix => Value[..12];
}

internal sealed record RuntimeMaterializationCacheSlot(
    string Target,
    RuntimeWasmOptimization Optimization);

internal enum RuntimeMaterializationCacheOutcome
{
    Miss,
    Hit,
    Corrupt,
}

internal sealed record RuntimeMaterializationCacheRead(
    RuntimeMaterializationCacheOutcome Outcome,
    byte[]? Bytes,
    string? Sha256);

internal sealed record RuntimeMaterializationCacheMetrics(
    string Stage,
    string KeyPrefix,
    RuntimeMaterializationCacheOutcome Outcome,
    bool Recomputed,
    long Bytes,
    double LookupMilliseconds,
    double TotalMilliseconds);

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
    long MaximumMemorySizeBytes,
    RuntimeMaterializationCacheMetrics CacheMetrics);
