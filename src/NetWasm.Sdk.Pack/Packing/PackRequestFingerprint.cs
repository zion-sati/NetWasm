using System.Text.Json.Serialization;
using NetWasm.Sdk.Pack.Packages;
using NetWasm.Sdk.Pack.Profiles;

namespace NetWasm.Sdk.Pack.Packing;

// The JSON a pack request fingerprint hashes. The names are those the
// fingerprint has always used, so an unchanged request keeps its fingerprint.
internal sealed record PackRequestFingerprint(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("identity")] PackageIdentity Identity,
    [property: JsonPropertyName("output")] string Output,
    [property: JsonPropertyName("nuspecOutput")] string NuspecOutput,
    [property: JsonPropertyName("manifestOutput")] string ManifestOutput,
    [property: JsonPropertyName("metadata")] PackMetadataFingerprint Metadata,
    [property: JsonPropertyName("targets")] PackTargetFingerprint[] Targets,
    [property: JsonPropertyName("targetIdentities")] TargetOutputIdentity[] TargetIdentities,
    [property: JsonPropertyName("files")] PackFileFingerprint[] Files,
    [property: JsonPropertyName("dependencies")] PackDependencyGroupFingerprint[] Dependencies,
    [property: JsonPropertyName("symbols")] PackSymbolsFingerprint Symbols,
    [property: JsonPropertyName("source")] PackSourceFingerprint Source,
    [property: JsonPropertyName("restore")] PackRestoreFingerprint Restore,
    [property: JsonPropertyName("determinism")] DeterminismPolicy Determinism,
    bool SuppressDependencies);

internal sealed record PackMetadataFingerprint(
    string Authors,
    string Description,
    string? Title,
    string? Owners,
    string? Summary,
    string? ProjectUrl,
    string? LicenseExpression,
    string? LicenseFile,
    string? Icon,
    string? Readme,
    string? Copyright,
    string? Tags,
    string? ReleaseNotes,
    string? RepositoryUrl,
    string? RepositoryType,
    string? RepositoryBranch,
    string? RepositoryCommit,
    string? PackageTypes,
    bool DevelopmentDependency,
    bool Serviceable,
    bool PublishRepositoryUrl);

internal sealed record PackTargetFingerprint(
    string Alias,
    string Identifier,
    string Version,
    string CanonicalFolder,
    string CanonicalDependencyGroup);

internal sealed record PackFileFingerprint(
    string TargetPath,
    PackageFileKind Kind,
    string? TargetFrameworkAlias,
    string? ExpectedSha256,
    long? ExpectedLength,
    string ContentHash);

internal sealed record PackSymbolFileFingerprint(
    string TargetPath,
    string Format,
    string? TargetFrameworkAlias,
    string? ExpectedSha256,
    long? ExpectedLength,
    string ContentHash);

internal sealed record PackSourceFileFingerprint(
    string TargetPath,
    string? TargetFrameworkAlias,
    string? ExpectedSha256,
    long? ExpectedLength,
    string ContentHash);

internal sealed record PackDependencyGroupFingerprint(
    string TargetFramework,
    string? TargetFrameworkAlias,
    CanonicalPackageDependency[] Dependencies);

internal sealed record PackSymbolsFingerprint(bool IncludeSymbols, string Format, PackSymbolFileFingerprint[] Files);

internal sealed record PackSourceFingerprint(
    bool IncludeSource,
    bool PublishRepositoryUrl,
    string? SourceLinkHash,
    PackSourceFileFingerprint[] Files);

internal sealed record PackRestoreFingerprint(
    string AssetsFileHash,
    string? LockFileHash,
    string[] TargetKeys,
    string NuGetVersion,
    string SdkVersion,
    bool Required,
    PackRestoreEdgeFingerprint[] Graph);

internal sealed record PackRestoreEdgeFingerprint(
    string Id,
    string Version,
    string? VersionRange,
    string? ResolvedVersion,
    string TargetFramework,
    bool IsPrivate,
    bool IsDevelopmentDependency,
    string? Source);
