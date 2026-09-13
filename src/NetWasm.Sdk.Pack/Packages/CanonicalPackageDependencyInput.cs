namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageDependencyInput(
    string Id,
    string VersionRange,
    string TargetFramework)
{
    public string? TargetFrameworkAlias { get; init; }
    public string IncludeAssets { get; init; } = "all";
    public string ExcludeAssets { get; init; } = "none";
    public string PrivateAssets { get; init; } = "none";
    public bool IsDevelopmentDependency { get; init; }
    public PackageDependencyOrigin Origin { get; init; } = PackageDependencyOrigin.PackageReference;
}
