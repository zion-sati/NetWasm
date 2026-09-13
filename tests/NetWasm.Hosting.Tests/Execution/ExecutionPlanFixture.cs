using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

internal sealed class ExecutionContractDefinitionStub(
    string key,
    Action<DeploymentManifest>? validate = null,
    IApplicationExecutionStrategy? strategy = null) : IExecutionContractDefinition
{
    public string Key { get; } = key;

    public IApplicationExecutionStrategy Strategy { get; } = strategy ?? new ApplicationExecutionStrategyStub(
        (_, _) => ValueTask.FromResult(ExecutionResultFixture.Normal()));

    public void Validate(DeploymentManifest manifest) => validate?.Invoke(manifest);
}

internal sealed class MissingStrategyExecutionContractDefinitionStub(string key) : IExecutionContractDefinition
{
    public string Key { get; } = key;

    public IApplicationExecutionStrategy Strategy => null!;

    public void Validate(DeploymentManifest manifest)
    {
    }
}

internal sealed class ExecutionContractRegistryStub(Func<string, IExecutionContractDefinition> resolve) : IExecutionContractRegistry
{
    public IExecutionContractDefinition Resolve(string key) => resolve(key);
}

internal sealed class ApplicationExecutionStrategyStub(
    Func<ApplicationExecutionContext, CancellationToken, ValueTask<NetWasmExecutionResult>> execute)
    : IApplicationExecutionStrategy
{
    public ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ApplicationExecutionContext context,
        CancellationToken cancellationToken) => execute(context, cancellationToken);
}
