using System.Collections.Immutable;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Tests.Capabilities;
using NetWasm.Hosting.Tests.Deployment;

namespace NetWasm.Hosting.Tests.Execution;

internal sealed record ApplicationExecutionFixtureData(
    ApplicationExecutionRequest Request,
    DeploymentManifest Manifest,
    ExecutionPlan Plan,
    NetWasmExecutionResult Result);

internal static class ApplicationExecutionFixture
{
    internal static ApplicationExecutionFixtureData Create(IApplicationExecutionStrategy? strategy = null)
    {
        var binding = CapabilityBindingFixture.Create();
        var descriptor = ManifestFixture.CreateDescriptor(
            binding.Manifest,
            binding.Request.DeploymentManifestSha256);
        var freshness = new DeploymentFreshnessRequest(
            descriptor,
            new byte[] { 1 },
            ManifestFixture.CreateSnapshots(binding.Manifest).ToImmutableArray());
        var executionRequest = new ApplicationExecutionRequest(
            freshness,
            binding.Request,
            binding.PlatformProviders,
            binding.ApplicationProviders);
        var selectedStrategy = strategy ?? new ApplicationExecutionStrategyStub(
            (_, _) => ValueTask.FromResult(ExecutionResultFixture.Normal()));
        var definition = new ExecutionContractDefinitionStub(
            binding.Manifest.ExecutionContract,
            strategy: selectedStrategy);
        return new(
            executionRequest,
            binding.Manifest,
            new(binding.Manifest, definition, selectedStrategy),
            ExecutionResultFixture.Normal(-7));
    }
}

internal sealed class DeploymentFreshnessValidatorStub(
    Func<DeploymentFreshnessRequest, DeploymentManifest> validate) : IDeploymentFreshnessValidator
{
    public DeploymentManifest Validate(DeploymentFreshnessRequest request) => validate(request);
}

internal sealed class ExecutionPlanBuilderStub(Func<DeploymentManifest, ExecutionPlan> build) : IExecutionPlanBuilder
{
    public ExecutionPlan Build(DeploymentManifest manifest) => build(manifest);
}

internal sealed class CapabilityBindingValidatorStub(
    Action<DeploymentManifest, NetWasmExecutionRequest,
        System.Collections.Immutable.ImmutableArray<NetWasmPlatformImportProvider>,
        System.Collections.Immutable.ImmutableArray<NetWasmApplicationImportProvider>> validate)
    : ICapabilityBindingValidator
{
    public void Validate(
        DeploymentManifest manifest,
        NetWasmExecutionRequest request,
        System.Collections.Immutable.ImmutableArray<NetWasmPlatformImportProvider> platformProviders,
        System.Collections.Immutable.ImmutableArray<NetWasmApplicationImportProvider> applicationProviders) =>
        validate(manifest, request, platformProviders, applicationProviders);
}
