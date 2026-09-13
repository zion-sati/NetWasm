using NetWasm.Hosting.Capabilities;

namespace NetWasm.Hosting.Tests.Capabilities;

public sealed class InternalImportPolicyTests
{
    private readonly InternalImportPolicy _subject = new();

    [Theory]
    [InlineData("netwasm:runtime/reactor-host")]
    [InlineData("netwasm:runtime/reactor-host@1.0.0")]
    public void RecognizesExactRuntimeOwnedReactorModules(string module)
    {
        Assert.True(_subject.IsInternal(module));
    }

    [Theory]
    [InlineData("netwasm:runtime/reactor-host@1.0.1")]
    [InlineData("netwasm:runtime/reactor-host@1")]
    [InlineData("netwasm:runtime/reactor-hosted@1.0.0")]
    [InlineData("example:runtime/reactor-host@1.0.0")]
    public void LeavesEveryOtherModuleAtTheExternalProviderBoundary(string module)
    {
        Assert.False(_subject.IsInternal(module));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingModuleIdentity(string? module)
    {
        Assert.ThrowsAny<ArgumentException>(() => _subject.IsInternal(module!));
    }
}
