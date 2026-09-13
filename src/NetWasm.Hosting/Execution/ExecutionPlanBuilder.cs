using System;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

/// <summary>Validates and binds a deployment to one exact execution contract before guest start.</summary>
internal sealed class ExecutionPlanBuilder : IExecutionPlanBuilder
{
    private readonly IDeploymentManifestValidator _manifestValidator;
    private readonly IExecutionContractRegistry _contracts;

    internal ExecutionPlanBuilder(
        IDeploymentManifestValidator manifestValidator,
        IExecutionContractRegistry contracts)
    {
        _manifestValidator = manifestValidator ?? throw new ArgumentNullException(nameof(manifestValidator));
        _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    }

    public ExecutionPlan Build(DeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        _manifestValidator.Validate(manifest);
        var contract = _contracts.Resolve(manifest.ExecutionContract);
        contract.Validate(manifest);
        var strategy = contract.Strategy
            ?? throw new InvalidOperationException("The selected execution contract has no execution strategy.");
        return new(manifest, contract, strategy);
    }
}
