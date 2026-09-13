namespace NetWasm.Hosting.Deployment;

/// <summary>The observed digest of one artifact supplied to an in-memory freshness check.</summary>
public sealed record DeploymentArtifactSnapshot(string RelativePath, string Sha256);
