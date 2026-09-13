namespace NetWasm.Sdk.Pack.Provenance;

public sealed record SdkProvenanceFileInput(
    string SourcePath,
    string PackagePath,
    string? LogicalSourcePath = null);

public sealed record SdkProvenanceManifestInput(
    string PackageId,
    string PackageVersion,
    string SourceRevision,
    string ManifestPackagePath,
    IReadOnlyList<SdkProvenanceFileInput> PackageFiles,
    IReadOnlyList<SdkProvenanceFileInput> SourceFiles);
