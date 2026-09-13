using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

internal sealed record ApplicationExecutionRequest(
    DeploymentFreshnessRequest Deployment,
    NetWasmExecutionRequest Execution,
    ImmutableArray<NetWasmPlatformImportProvider> PlatformProviders,
    ImmutableArray<NetWasmApplicationImportProvider> ApplicationProviders);
