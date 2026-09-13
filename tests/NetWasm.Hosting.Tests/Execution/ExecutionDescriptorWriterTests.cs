using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ExecutionDescriptorWriterTests
{
    [Fact]
    public void WritesDeterministicUtf8AndPreservesPaths()
    {
        var descriptor = DescriptorFixture.Create();
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));
        var bytes = subject.Write(descriptor);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(bytes, subject.Write(descriptor));
        Assert.StartsWith("{\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'{', bytes[0]);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(descriptor.LauncherPath, json.RootElement.GetProperty("launcherPath").GetString());
        Assert.Equal(descriptor.DeploymentManifestPath, json.RootElement.GetProperty("deploymentManifestPath").GetString());
        var reader = Assert.IsAssignableFrom<IExecutionDescriptorReader>(new ExecutionDescriptorReader(new ExecutionDescriptorValidator()));
        var restored = reader.Read(bytes);
        Assert.Equal(descriptor with { ToolPackages = restored.ToolPackages }, restored);
        Assert.Equal<ExecutionToolPackage>(descriptor.ToolPackages, restored.ToolPackages);
    }

    [Fact]
    public void CanonicalizesPackageOrderWithoutMutatingInput()
    {
        var descriptor = DescriptorFixture.Create();
        var first = descriptor.ToolPackages[0] with { Id = "A.Tools" };
        var last = descriptor.ToolPackages[0] with { Id = "Z.Tools" };
        descriptor = descriptor with { ToolPackages = [last, first] };
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new ExecutionDescriptorValidator()));
        Assert.Equal(subject.Write(descriptor with { ToolPackages = [first, last] }), subject.Write(descriptor));
        Assert.Same(last, descriptor.ToolPackages[0]);
    }

    [Fact]
    public void PropagatesValidationFailureBeforeSerializing()
    {
        var descriptor = DescriptorFixture.Create();
        var failure = new ArgumentException("Rejected descriptor.");
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new DescriptorValidationStub(value =>
        {
            Assert.Same(descriptor, value);
            throw failure;
        })));
        Assert.Same(failure, Assert.Throws<ArgumentException>(() => subject.Write(descriptor)));
    }

    [Fact]
    public void RejectsNullInputBeforeDelegation()
    {
        var subject = Assert.IsAssignableFrom<IExecutionDescriptorWriter>(new ExecutionDescriptorWriter(new DescriptorValidationStub(_ => Assert.Fail("Must not delegate."))));
        Assert.Throws<ArgumentNullException>(() => subject.Write(null!));
    }

    [Fact]
    public void RejectsMissingValidator() => Assert.Throws<ArgumentNullException>(() => new ExecutionDescriptorWriter(null!));
}
