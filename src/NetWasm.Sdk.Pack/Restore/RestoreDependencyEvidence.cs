namespace NetWasm.Sdk.Pack.Restore;

public sealed record RestoreDependencyEvidence(
    string Id,
    string Version,
    string TargetFramework,
    bool IsPrivate,
    bool IsDevelopmentDependency)
{
    public string? VersionRange { get; init; }
    public string? ResolvedVersion { get; init; }
    public string? Source { get; init; }
}
