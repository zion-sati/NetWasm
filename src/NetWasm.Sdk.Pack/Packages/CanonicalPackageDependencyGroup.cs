using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Packages;

public sealed record CanonicalPackageDependencyGroup(
    string TargetFramework,
    ImmutableArray<CanonicalPackageDependency> Dependencies)
{
    public string? TargetFrameworkAlias { get; init; }
}
