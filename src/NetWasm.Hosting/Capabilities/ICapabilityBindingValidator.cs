using System.Collections.Immutable;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Capabilities;

public interface ICapabilityBindingValidator
{
    void Validate(
        DeploymentManifest manifest,
        NetWasmExecutionRequest request,
        ImmutableArray<NetWasmPlatformImportProvider> platformProviders,
        ImmutableArray<NetWasmApplicationImportProvider> applicationProviders);
}
