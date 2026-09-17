using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Planning;

public sealed record RuntimeLinkPlanRequest(
    string ManifestJson,
    string Target,
    long ApplicationStaticDataEnd,
    string AssetRoot = "/runtime",
    string OutputPath = "/runtime-linked.wasm",
    long? InitialHeapSizeBytes = null,
    long? MaximumMemorySizeBytes = null,
    ImmutableArray<RuntimeLinkPlanAsset> SystemLibraries = default);

public sealed record RuntimeLinkPlanAsset(string Path, string Sha256);

public sealed record RuntimeLinkPlan(
    ImmutableArray<string> Arguments,
    ImmutableArray<RuntimeLinkPlanAsset> Inputs,
    string RuntimeAbi,
    string ToolchainFingerprint,
    long RuntimeGlobalBase,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes);
