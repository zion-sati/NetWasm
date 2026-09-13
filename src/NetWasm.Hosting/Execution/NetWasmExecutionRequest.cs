using System.Collections.Immutable;

namespace NetWasm.Hosting.Execution;

/// <summary>The serializable, ephemeral value inputs for one NetWasm execution.</summary>
public sealed record NetWasmExecutionRequest(
    int SchemaVersion,
    string BuildFingerprint,
    string DeploymentManifestSha256,
    ImmutableArray<string> Arguments,
    ImmutableArray<NetWasmEnvironmentVariable> Environment,
    NetWasmCapabilityGrants Grants,
    ImmutableArray<NetWasmApplicationImport> ApplicationImports);
