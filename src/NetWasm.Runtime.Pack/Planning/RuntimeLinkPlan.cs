using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Planning;

public enum RuntimeWasmOptimization
{
    None,
    O0,
    O1,
    O2,
    O3,
    Os,
    Oz,
}

public sealed record RuntimeLinkPlanRequest(
    string ManifestJson,
    string Target,
    long ApplicationStaticDataEnd,
    string AssetRoot = "/runtime",
    string OutputPath = "/runtime-linked.wasm",
    long? InitialHeapSizeBytes = null,
    long? MaximumMemorySizeBytes = null,
    ImmutableArray<RuntimeLinkPlanAsset> SystemLibraries = default,
    RuntimeWasmOptimization Optimization = RuntimeWasmOptimization.Oz)
{
    // A default array preserves the complete legacy runtime when an older
    // compiler cannot report feature use. An initialized array is authoritative,
    // including an empty array for applications that need no optional runtime
    // capabilities.
    public ImmutableArray<string> RuntimeFeatures { get; init; }

    public string? GarbageCollector { get; init; }

    /// <summary>Reached native calls. An initialized empty array selects no native providers.</summary>
    public ImmutableArray<RuntimeLinkPlanNativeImport> NativeImports { get; init; } = [];

    /// <summary>Caller-validated regular archives available to satisfy reached native calls.</summary>
    public ImmutableArray<RuntimeLinkPlanNativeLibrary> NativeLibraries { get; init; } = [];
}

public sealed record RuntimeLinkPlanAsset(string Path, string Sha256);

public sealed record RuntimeLinkPlanExport(string Name, byte Kind);

public enum RuntimeLinkPlanNativeValueType
{
    I32,
    I64,
    F32,
    F64,
}

public sealed record RuntimeLinkPlanNativeImport(
    string LibraryName,
    string EntryPoint,
    ImmutableArray<RuntimeLinkPlanNativeValueType> Parameters,
    RuntimeLinkPlanNativeValueType? ReturnType);

public sealed record RuntimeLinkPlanNativeLibrary(
    string LibraryName,
    string Target,
    string Path,
    string Sha256);

public sealed record RuntimeMaterializationCacheDescriptor(
    string Schema,
    string Namespace,
    string Slot,
    string Key);

public sealed record RuntimeLinkPlan(
    ImmutableArray<string> Arguments,
    ImmutableArray<string> OptimizationArguments,
    ImmutableArray<RuntimeLinkPlanAsset> Inputs,
    string RuntimeAbi,
    string ToolchainFingerprint,
    long RuntimeGlobalBase,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes,
    RuntimeMaterializationCacheDescriptor Cache)
{
    public string GarbageCollector { get; init; } = string.Empty;

    /// <summary>Temporary runtime exports that a raw-module consumer must remove after linking.</summary>
    public ImmutableArray<RuntimeLinkPlanExport> InternalRuntimeExports { get; init; } = [];
}
