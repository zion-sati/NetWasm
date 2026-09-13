using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ApplicationExecutorTests
{
    [Fact]
    public async Task ExecutesRetainedStrategyAfterOrderedValidationAndReturnsItsValidResult()
    {
        var events = new List<string>();
        ApplicationExecutionFixtureData? data = null;
        var strategy = new ApplicationExecutionStrategyStub((context, token) =>
        {
            Assert.Same(data!.Plan, context.Plan);
            Assert.Same(data.Request.Execution, context.Request);
            Assert.Equal<NetWasmPlatformImportProvider>(data.Request.PlatformProviders, context.PlatformProviders);
            Assert.Equal<NetWasmApplicationImportProvider>(data.Request.ApplicationProviders, context.ApplicationProviders);
            Assert.False(token.IsCancellationRequested);
            events.Add("strategy");
            return ValueTask.FromResult(data.Result);
        });
        data = ApplicationExecutionFixture.Create(strategy);
        var subject = CreateSubject(data, events);

        var result = await subject.ExecuteAsync(data.Request, CancellationToken.None);

        Assert.Same(data.Result, result);
        Assert.Equal(["freshness", "plan", "capability", "strategy", "result"], events);
    }

    [Fact]
    public async Task PreCancellationReturnsValidatedResultWithoutInvokingExecutionDependencies()
    {
        var data = ApplicationExecutionFixture.Create();
        var validations = 0;
        var subject = new ApplicationExecutor(
            new DeploymentFreshnessValidatorStub(_ => throw new Xunit.Sdk.XunitException("Must not validate freshness.")),
            new ExecutionPlanBuilderStub(_ => throw new Xunit.Sdk.XunitException("Must not build a plan.")),
            new CapabilityBindingValidatorStub((_, _, _, _) => Assert.Fail("Must not validate capabilities.")),
            new ExecutionResultValidationStub(result =>
            {
                new NetWasmExecutionResultValidator().Validate(result);
                validations++;
            }));

        var result = await subject.ExecuteAsync(data.Request, new CancellationToken(true));

        AssertFailure(result, NetWasmCompletionKind.CallerCancellation, NetWasmFailurePhase.Validation, "caller.cancelled");
        Assert.Equal(1, validations);
    }

    [Theory]
    [InlineData("freshness")]
    [InlineData("manifest-identity")]
    [InlineData("plan")]
    [InlineData("capability")]
    public async Task ContractFailurePreventsStrategyInvocation(string stage)
    {
        var data = ApplicationExecutionFixture.Create(new ApplicationExecutionStrategyStub(
            (_, _) => throw new Xunit.Sdk.XunitException("Must not execute an invalid contract.")));
        if (stage == "manifest-identity")
        {
            data = data with
            {
                Request = data.Request with
                {
                    Execution = data.Request.Execution with { DeploymentManifestSha256 = new string('f', 64) },
                },
            };
        }

        var subject = new ApplicationExecutor(
            new DeploymentFreshnessValidatorStub(_ => stage == "freshness"
                ? throw new ArgumentException("Stale deployment.")
                : data.Manifest),
            new ExecutionPlanBuilderStub(_ => stage == "plan"
                ? throw new NotSupportedException("Unknown contract.")
                : data.Plan),
            new CapabilityBindingValidatorStub((_, _, _, _) =>
            {
                if (stage == "capability")
                {
                    throw new InvalidOperationException("Denied capability.");
                }
            }),
            new NetWasmExecutionResultValidator());

        var result = await subject.ExecuteAsync(data.Request, CancellationToken.None);

        AssertFailure(result, NetWasmCompletionKind.ContractFailure, NetWasmFailurePhase.Validation, "contract.invalid");
        Assert.DoesNotContain("Stale", result.PrimaryFailure!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown", result.PrimaryFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Denied", result.PrimaryFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedValidationFailureReturnsHostFailureWithoutStarting()
    {
        var data = ApplicationExecutionFixture.Create(new ApplicationExecutionStrategyStub(
            (_, _) => throw new Xunit.Sdk.XunitException("Must not start.")));
        var subject = new ApplicationExecutor(
            new DeploymentFreshnessValidatorStub(_ => throw new InvalidDataException("private detail")),
            new ExecutionPlanBuilderStub(_ => data.Plan),
            new CapabilityBindingValidatorStub((_, _, _, _) => { }),
            new NetWasmExecutionResultValidator());

        var result = await subject.ExecuteAsync(data.Request, CancellationToken.None);

        AssertFailure(result, NetWasmCompletionKind.HostFailure, NetWasmFailurePhase.Validation, "host.validation");
        Assert.DoesNotContain("private detail", result.PrimaryFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedStrategyFailureReturnsHostFailure()
    {
        var data = ApplicationExecutionFixture.Create(new ApplicationExecutionStrategyStub(
            (_, _) => throw new InvalidDataException("private detail")));
        var subject = CreateSubject(data);

        var result = await subject.ExecuteAsync(data.Request, CancellationToken.None);

        AssertFailure(result, NetWasmCompletionKind.HostFailure, NetWasmFailurePhase.Execution, "host.execution");
        Assert.DoesNotContain("private detail", result.PrimaryFailure!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StrategyCancellationReturnsCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var data = ApplicationExecutionFixture.Create(new ApplicationExecutionStrategyStub((_, token) =>
        {
            cancellation.Cancel();
            return ValueTask.FromException<NetWasmExecutionResult>(new OperationCanceledException(token));
        }));
        var subject = CreateSubject(data);

        var result = await subject.ExecuteAsync(data.Request, cancellation.Token);

        AssertFailure(result, NetWasmCompletionKind.CallerCancellation, NetWasmFailurePhase.Execution, "caller.cancelled");
    }

    [Fact]
    public async Task MalformedStrategyResultReturnsHostFailure()
    {
        var data = ApplicationExecutionFixture.Create(new ApplicationExecutionStrategyStub(
            (_, _) => ValueTask.FromResult(ExecutionResultFixture.Normal() with { ExitCode = null })));
        var subject = CreateSubject(data);

        var result = await subject.ExecuteAsync(data.Request, CancellationToken.None);

        AssertFailure(result, NetWasmCompletionKind.HostFailure, NetWasmFailurePhase.Execution, "host.execution");
    }

    [Fact]
    public async Task RejectsNullRequestBeforeDelegation()
    {
        var data = ApplicationExecutionFixture.Create();
        var subject = new ApplicationExecutor(
            new DeploymentFreshnessValidatorStub(_ => throw new Xunit.Sdk.XunitException("Must not delegate.")),
            new ExecutionPlanBuilderStub(_ => throw new Xunit.Sdk.XunitException("Must not delegate.")),
            new CapabilityBindingValidatorStub((_, _, _, _) => Assert.Fail("Must not delegate.")),
            new ExecutionResultValidationStub(_ => Assert.Fail("Must not delegate.")));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await subject.ExecuteAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData("freshness")]
    [InlineData("plan")]
    [InlineData("capability")]
    [InlineData("result")]
    public void RejectsMissingDependency(string dependency)
    {
        var data = ApplicationExecutionFixture.Create();
        var freshness = dependency == "freshness" ? null : new DeploymentFreshnessValidatorStub(_ => data.Manifest);
        var plan = dependency == "plan" ? null : new ExecutionPlanBuilderStub(_ => data.Plan);
        var capability = dependency == "capability" ? null : new CapabilityBindingValidatorStub((_, _, _, _) => { });
        var result = dependency == "result" ? null : new NetWasmExecutionResultValidator();
        Assert.Throws<ArgumentNullException>(() => new ApplicationExecutor(freshness!, plan!, capability!, result!));
    }

    private static ApplicationExecutor CreateSubject(ApplicationExecutionFixtureData data, List<string>? events = null) =>
        new(
            new DeploymentFreshnessValidatorStub(request =>
            {
                Assert.Same(data.Request.Deployment, request);
                events?.Add("freshness");
                return data.Manifest;
            }),
            new ExecutionPlanBuilderStub(manifest =>
            {
                Assert.Same(data.Manifest, manifest);
                events?.Add("plan");
                return data.Plan;
            }),
            new CapabilityBindingValidatorStub((manifest, request, platforms, applications) =>
            {
                Assert.Same(data.Manifest, manifest);
                Assert.Same(data.Request.Execution, request);
                Assert.Equal<NetWasmPlatformImportProvider>(data.Request.PlatformProviders, platforms);
                Assert.Equal<NetWasmApplicationImportProvider>(data.Request.ApplicationProviders, applications);
                events?.Add("capability");
            }),
            new ExecutionResultValidationStub(result =>
            {
                new NetWasmExecutionResultValidator().Validate(result);
                events?.Add("result");
            }));

    private static void AssertFailure(
        NetWasmExecutionResult result,
        NetWasmCompletionKind kind,
        NetWasmFailurePhase phase,
        string code)
    {
        new NetWasmExecutionResultValidator().Validate(result);
        Assert.Equal(kind, result.CompletionKind);
        Assert.Equal(phase, result.PrimaryFailure!.Phase);
        Assert.Equal(code, result.PrimaryFailure.Code);
    }
}
