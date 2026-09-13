namespace NetWasm.Hosting.Deployment;

/// <summary>An immutable file in the deployable artifact closure.</summary>
public sealed record DeploymentArtifact(
    string RelativePath,
    string Role,
    string MediaType,
    string Sha256,
    int? SchemaVersion);
