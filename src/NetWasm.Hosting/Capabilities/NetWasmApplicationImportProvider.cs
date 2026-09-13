using System.Collections.Immutable;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Capabilities;

/// <summary>Immutable metadata for one selected application-owned import module.</summary>
public sealed record NetWasmApplicationImportProvider(
    string Module,
    ImmutableArray<DeploymentFunction> Functions);
