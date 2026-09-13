using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Tests.Deployment;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ExecutionPlanBuilderTests
{
    [Fact]
    public void ValidatesResolvesAndBindsOneExactContractInOrder()
    {
        var manifest = ManifestFixture.Create();
        var events = new List<string>();
        var definition = new ExecutionContractDefinitionStub(manifest.ExecutionContract, value =>
        {
            Assert.Same(manifest, value);
            events.Add("contract");
        });
        var registry = new ExecutionContractRegistryStub(key =>
        {
            Assert.Equal(manifest.ExecutionContract, key);
            events.Add("registry");
            return definition;
        });
        var validator = new ManifestValidationStub(value =>
        {
            Assert.Same(manifest, value);
            events.Add("manifest");
        });
        var subject = Assert.IsAssignableFrom<IExecutionPlanBuilder>(new ExecutionPlanBuilder(validator, registry));
        var plan = subject.Build(manifest);
        Assert.Same(manifest, plan.Manifest);
        Assert.Same(definition, plan.Contract);
        Assert.Same(definition.Strategy, plan.Strategy);
        Assert.Equal(["manifest", "registry", "contract"], events);
    }

    [Fact]
    public void RejectsNullManifestBeforeDelegation()
    {
        var subject = new ExecutionPlanBuilder(
            new ManifestValidationStub(_ => Assert.Fail("Must not validate null.")),
            new ExecutionContractRegistryStub(_ =>
            {
                Assert.Fail("Must not resolve null manifest.");
                return null!;
            }));
        Assert.Throws<ArgumentNullException>(() => subject.Build(null!));
    }

    [Fact]
    public void ManifestFailurePreventsResolution()
    {
        var manifest = ManifestFixture.Create();
        var failure = new ArgumentException("Invalid manifest.");
        var subject = new ExecutionPlanBuilder(
            new ManifestValidationStub(_ => throw failure),
            new ExecutionContractRegistryStub(_ =>
            {
                Assert.Fail("Must not resolve invalid manifest.");
                return null!;
            }));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Build(manifest)));
    }

    [Fact]
    public void MissingContractPreventsShapeValidation()
    {
        var manifest = ManifestFixture.Create();
        var failure = new NotSupportedException("Missing contract.");
        var subject = new ExecutionPlanBuilder(
            new ManifestValidationStub(_ => { }),
            new ExecutionContractRegistryStub(_ => throw failure));
        Assert.Same(failure, Assert.Throws<NotSupportedException>(() => subject.Build(manifest)));
    }

    [Fact]
    public void PropagatesSelectedContractShapeFailure()
    {
        var manifest = ManifestFixture.Create();
        var failure = new ArgumentException("Incomplete exports.");
        var definition = new ExecutionContractDefinitionStub(manifest.ExecutionContract, _ => throw failure);
        var subject = new ExecutionPlanBuilder(
            new ManifestValidationStub(_ => { }),
            new ExecutionContractRegistryStub(_ => definition));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Build(manifest)));
    }

    [Fact]
    public void RejectsSelectedContractWithoutStrategy()
    {
        var manifest = ManifestFixture.Create();
        var subject = new ExecutionPlanBuilder(
            new ManifestValidationStub(_ => { }),
            new ExecutionContractRegistryStub(_ => new MissingStrategyExecutionContractDefinitionStub(manifest.ExecutionContract)));
        Assert.Throws<InvalidOperationException>(() => subject.Build(manifest));
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("registry")]
    public void RejectsMissingDependency(string dependency)
    {
        var validator = dependency == "manifest" ? null : new DeploymentManifestValidator();
        var registry = dependency == "registry"
            ? null
            : new ExecutionContractRegistry([new ExecutionContractDefinitionStub("wasi-command@0.2.11")]);
        Assert.Throws<ArgumentNullException>(() => new ExecutionPlanBuilder(validator!, registry!));
    }
}
