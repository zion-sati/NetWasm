namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageDependency(string Id, string VersionRange)
{
    public string IncludeAssets { get; init; } = "all";
    public string ExcludeAssets { get; init; } = "none";
    public string PrivateAssets { get; init; } = "none";
}
