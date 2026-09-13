using System;
using System.Threading;
using System.Threading.Tasks;
using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Execution;

/// <summary>Coordinates validated execution without discovering artifacts, providers or strategies.</summary>
internal sealed class ApplicationExecutor : IApplicationExecutor
{
    private readonly IDeploymentFreshnessValidator _freshnessValidator;
    private readonly IExecutionPlanBuilder _planBuilder;
    private readonly ICapabilityBindingValidator _capabilityValidator;
    private readonly INetWasmExecutionResultValidator _resultValidator;

    internal ApplicationExecutor(
        IDeploymentFreshnessValidator freshnessValidator,
        IExecutionPlanBuilder planBuilder,
        ICapabilityBindingValidator capabilityValidator,
        INetWasmExecutionResultValidator resultValidator)
    {
        _freshnessValidator = freshnessValidator ?? throw new ArgumentNullException(nameof(freshnessValidator));
        _planBuilder = planBuilder ?? throw new ArgumentNullException(nameof(planBuilder));
        _capabilityValidator = capabilityValidator ?? throw new ArgumentNullException(nameof(capabilityValidator));
        _resultValidator = resultValidator ?? throw new ArgumentNullException(nameof(resultValidator));
    }

    public async ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ApplicationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return CreateFailure(
                NetWasmCompletionKind.CallerCancellation,
                NetWasmFailurePhase.Validation,
                "caller.cancelled",
                "Execution was cancelled by the caller.");
        }

        ExecutionPlan plan;
        try
        {
            var manifest = _freshnessValidator.Validate(request.Deployment);
            if (!string.Equals(
                    request.Execution.DeploymentManifestSha256,
                    request.Deployment.ExecutionDescriptor.DeploymentManifestSha256,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("The execution request names a different deployment manifest.", nameof(request));
            }

            plan = _planBuilder.Build(manifest);
            _capabilityValidator.Validate(
                manifest,
                request.Execution,
                request.PlatformProviders,
                request.ApplicationProviders);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return CreateFailure(
                NetWasmCompletionKind.ContractFailure,
                NetWasmFailurePhase.Validation,
                "contract.invalid",
                "Execution contract validation failed.");
        }
        catch (Exception)
        {
            return CreateFailure(
                NetWasmCompletionKind.HostFailure,
                NetWasmFailurePhase.Validation,
                "host.validation",
                "The execution host failed during validation.");
        }

        try
        {
            var context = new ApplicationExecutionContext(
                plan,
                request.Execution,
                request.PlatformProviders,
                request.ApplicationProviders);
            var result = await plan.Strategy.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            _resultValidator.Validate(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateFailure(
                NetWasmCompletionKind.CallerCancellation,
                NetWasmFailurePhase.Execution,
                "caller.cancelled",
                "Execution was cancelled by the caller.");
        }
        catch (Exception)
        {
            return CreateFailure(
                NetWasmCompletionKind.HostFailure,
                NetWasmFailurePhase.Execution,
                "host.execution",
                "The execution host failed.");
        }
    }

    private NetWasmExecutionResult CreateFailure(
        NetWasmCompletionKind completionKind,
        NetWasmFailurePhase phase,
        string code,
        string message)
    {
        var result = new NetWasmExecutionResult(
            1,
            completionKind,
            null,
            new(phase, code, message),
            []);
        _resultValidator.Validate(result);
        return result;
    }
}
