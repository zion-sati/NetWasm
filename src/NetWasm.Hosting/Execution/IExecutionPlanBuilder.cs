using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

internal interface IExecutionPlanBuilder
{
    ExecutionPlan Build(DeploymentManifest manifest);
}
