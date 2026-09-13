using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Packages;

/// <summary>
/// Immutable data crossing the MSBuild adapter. It contains no MSBuild types.
/// </summary>
public sealed record CanonicalPackInputs(
    string Id,
    string Version,
    string Authors,
    string Description,
    string OutputPath,
    IReadOnlyList<CanonicalPackageFileInput> Files,
    IReadOnlyList<CanonicalPackageDependencyInput> Dependencies,
    string CanonicalTargetFramework)
{
    public PackageMetadata Metadata { get; init; } = new(Authors, Description);
    public IReadOnlyList<TargetProfile> Targets { get; init; } = [];
    public IReadOnlyList<TargetOutputIdentity> TargetIdentities { get; init; } = [];
    public SymbolInputs Symbols { get; init; } = SymbolInputs.None;
    public SourceInputs Source { get; init; } = SourceInputs.None;
    public RestoreEvidence Restore { get; init; } = RestoreEvidence.Unspecified;
    public DeterminismPolicy Determinism { get; init; } = DeterminismPolicy.Default;
    public string? SymbolOutputPath { get; init; }
    public bool SuppressDependencies { get; init; }
    public bool NoBuild { get; init; }
    public string? ExpectedRequestHash { get; init; }
    public string? NuspecOutputPath { get; init; }
    public string? ManifestOutputPath { get; init; }
}
