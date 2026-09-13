using System.Collections.Immutable;

namespace NetWasm.Hosting.Deployment;

/// <summary>Publish-safe identity and complete immutable file closure for a NetWasm deployment.</summary>
public sealed record DeploymentManifest(
    int SchemaVersion,
    string SemanticBuildId,
    DeploymentKind DeploymentKind,
    string Profile,
    string Target,
    string FeatureSet,
    string ExecutionContract,
    DeploymentVersions Versions,
    string BuildFingerprint,
    ImmutableArray<string> RuntimeFeatures,
    ImmutableArray<DeploymentArtifact> Artifacts,
    ImmutableArray<string> RequiredImportModules,
    ImmutableArray<DeploymentFunction> RequiredImports,
    ImmutableArray<DeploymentFunction> Exports);
