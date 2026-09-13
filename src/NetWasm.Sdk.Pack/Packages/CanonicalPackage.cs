using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackage
{
    public CanonicalPackage(
        string id,
        string version,
        string authors,
        string description,
        string outputPath,
        ImmutableArray<CanonicalPackageFile> files,
        ImmutableArray<CanonicalPackageDependencyGroup> dependencyGroups)
    {
        Id = id;
        Version = version;
        Authors = authors;
        Description = description;
        OutputPath = outputPath;
        Files = files;
        DependencyGroups = dependencyGroups;
        Identity = new PackageIdentity(id, version);
        Metadata = new PackageMetadata(authors, description);
        Targets = [];
        TargetIdentities = [];
        Symbols = SymbolInputs.None;
        Source = SourceInputs.None;
        Restore = RestoreEvidence.Unspecified;
        Determinism = DeterminismPolicy.Default;
    }

    public string Id { get; }
    public string Version { get; }
    public string Authors { get; }
    public string Description { get; }
    public string OutputPath { get; init; }
    public ImmutableArray<CanonicalPackageFile> Files { get; init; }
    public ImmutableArray<CanonicalPackageDependencyGroup> DependencyGroups { get; init; }
    public PackageIdentity Identity { get; init; }
    public PackageMetadata Metadata { get; init; }
    public ImmutableArray<TargetProfile> Targets { get; init; }
    public ImmutableArray<TargetOutputIdentity> TargetIdentities { get; init; }
    public SymbolInputs Symbols { get; init; }
    public SourceInputs Source { get; init; }
    public RestoreEvidence Restore { get; init; }
    public DeterminismPolicy Determinism { get; init; }
    public string? SymbolOutputPath { get; init; }
    public bool SuppressDependencies { get; init; }
    public bool NoBuild { get; init; }
    public string? ExpectedRequestHash { get; init; }
    public string? NuspecOutputPath { get; init; }
    public string? ManifestOutputPath { get; init; }
}
