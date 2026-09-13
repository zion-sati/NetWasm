namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageFileInput(string SourcePath, string TargetPath)
{
    public string? TargetFrameworkAlias { get; init; }
    public PackageFileKind Kind { get; init; } = PackageFileKind.Other;
    public bool ExcludeFromMainPackage { get; init; }
    public string? SourceRoot { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedLength { get; init; }
}
