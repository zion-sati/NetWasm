namespace NetWasm.Sdk.Pack.Packages;

public sealed record PackageMetadata(
    string Authors,
    string Description,
    string? Title = null,
    string? Owners = null,
    string? Summary = null,
    string? ProjectUrl = null,
    string? LicenseExpression = null,
    string? LicenseFile = null,
    string? Icon = null,
    string? Readme = null,
    string? Copyright = null,
    string? Tags = null,
    string? ReleaseNotes = null,
    string? RepositoryUrl = null,
    string? RepositoryType = null,
    string? RepositoryBranch = null,
    string? RepositoryCommit = null,
    string? PackageTypes = null,
    bool DevelopmentDependency = false,
    bool Serviceable = false,
    bool PublishRepositoryUrl = false);
