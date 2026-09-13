using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

internal sealed record ExecutionPlan(
    DeploymentManifest Manifest,
    IExecutionContractDefinition Contract,
    IApplicationExecutionStrategy Strategy);
