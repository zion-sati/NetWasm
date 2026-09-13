namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageFile(string SourcePath, string TargetPath)
{
    public PackageFileKind Kind { get; init; } = PackageFileKind.Other;
    public string? TargetFrameworkAlias { get; init; }
    public string? SourceRoot { get; init; }
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedLength { get; init; }
}
