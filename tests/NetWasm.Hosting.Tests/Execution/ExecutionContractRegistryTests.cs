using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ExecutionContractRegistryTests
{
    [Fact]
    public void ResolvesOnlyExactRegisteredKey()
    {
        var expected = new ExecutionContractDefinitionStub("wasi-command@0.2.11");
        var other = new ExecutionContractDefinitionStub("netwasm-process@1.0.0");
        var subject = Assert.IsAssignableFrom<IExecutionContractRegistry>(new ExecutionContractRegistry([expected, other]));
        Assert.Same(expected, subject.Resolve(expected.Key));
        Assert.Same(other, subject.Resolve(other.Key));
        Assert.Throws<ArgumentException>(() => subject.Resolve("Wasi-command@0.2.11"));
        Assert.Throws<NotSupportedException>(() => subject.Resolve("wasi-command@0.2.12"));
    }

    [Fact]
    public void RejectsNullRegistrationSequence() =>
        Assert.Throws<ArgumentNullException>(() => new ExecutionContractRegistry(null!));

    [Fact]
    public void RejectsEmptyRegistrationSequence() =>
        Assert.Throws<ArgumentException>(() => new ExecutionContractRegistry([]));

    [Fact]
    public void RejectsNullRegistration() =>
        Assert.Throws<ArgumentNullException>(() => new ExecutionContractRegistry([null!]));

    [Fact]
    public void RejectsDuplicateExactKey()
    {
        var first = new ExecutionContractDefinitionStub("wasi-command@0.2.11");
        var duplicate = new ExecutionContractDefinitionStub(first.Key);
        Assert.Throws<ArgumentException>(() => new ExecutionContractRegistry([first, duplicate]));
    }

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public void RejectsMalformedRegistrationKey(string key) =>
        Assert.ThrowsAny<ArgumentException>(() => new ExecutionContractRegistry([new ExecutionContractDefinitionStub(key)]));

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public void RejectsMalformedResolutionKey(string key)
    {
        var subject = new ExecutionContractRegistry([new ExecutionContractDefinitionStub("wasi-command@0.2.11")]);
        Assert.ThrowsAny<ArgumentException>(() => subject.Resolve(key));
    }

    public static TheoryData<string> InvalidKeys() => new()
    {
        null!,
        "",
        " ",
        "missing-version",
        "@1.0.0",
        "wasi-command@",
        "a@b@1.0.0",
        "-wasi@1.0.0",
        " wasi@1.0.0",
        "Wasi@1.0.0",
        "wasi_command@1.0.0",
        "wasi@1.0.x",
        "wasi@1.0",
        "wasi@1..0",
    };
}
