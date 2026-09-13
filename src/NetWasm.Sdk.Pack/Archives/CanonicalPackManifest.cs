using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Archives;

public sealed partial record CanonicalPackManifest(
    string SchemaVersion,
    string TaskVersion,
    string RequestHash,
    ImmutableArray<string> EntryHashes,
    string PackageHash,
    string? SymbolPackageHash)
{
    public ImmutableArray<string> InputHashes { get; init; } = [];
    public ImmutableArray<string> TargetKeys { get; init; } = [];
    public string? PackagePath { get; init; }
    public string? SymbolPackagePath { get; init; }
}
