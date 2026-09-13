using System.Collections.Immutable;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Capabilities;

/// <summary>Immutable metadata for one selected NetWasm-owned platform module.</summary>
public sealed record NetWasmPlatformImportProvider(
    string Module,
    NetWasmPlatformCapability Capability,
    ImmutableArray<DeploymentFunction> Functions);
