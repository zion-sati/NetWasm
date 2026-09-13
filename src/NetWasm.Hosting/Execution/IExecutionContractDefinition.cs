using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

internal interface IExecutionContractDefinition
{
    string Key { get; }

    IApplicationExecutionStrategy Strategy { get; }

    void Validate(DeploymentManifest manifest);
}
