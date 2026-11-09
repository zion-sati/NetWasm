using System.Text;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class CanonicalComponentAdapterWriterTests
{
    private readonly CanonicalComponentAdapterWriter _subject = new();

    [Fact]
    public void WritesDeterministicDependencyFreeCommandAdapter()
    {
        var request = new CanonicalComponentAdapterRequest(
            CanonicalComponentAdapterWriter.CommandContract,
            CanonicalComponentAdapterWriter.SupportedJcoVersion);

        var first = _subject.Write(request);
        var second = _subject.Write(request);
        var source = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.StartsWith(
            $"// Generated for jco {CanonicalComponentAdapterWriter.SupportedJcoVersion}.\n",
            source,
            StringComparison.Ordinal);
        Assert.EndsWith("\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', source);
        Assert.DoesNotContain("import ", source, StringComparison.Ordinal);
        Assert.Contains("export function createAdapter(generatedModule)", source, StringComparison.Ordinal);
        Assert.Contains("generatedModule.instantiate.bind(generatedModule)", source, StringComparison.Ordinal);
        Assert.Contains("root?.[\"wasi:cli/run@0.3.2\"]", source, StringComparison.Ordinal);
        Assert.Contains("root[\"netwasm:diagnostics/command@1.0.0\"]", source, StringComparison.Ordinal);
        Assert.Contains("diagnosticCommand.run.bind(diagnosticCommand)", source, StringComparison.Ordinal);
        Assert.Contains("generatedModule._util?.isComponentError", source, StringComparison.Ordinal);
        Assert.Contains("isComponentError(error)", source, StringComparison.Ordinal);
        Assert.Contains("await command.run();", source, StringComparison.Ordinal);
        Assert.Contains("return 0;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("constructor?.name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("wasiCliRun", source, StringComparison.Ordinal);
        Assert.DoesNotContain("root.run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("netwasm:runtime/process", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnsupportedJcoBeforeContractSelection()
    {
        var request = new CanonicalComponentAdapterRequest(
            "unsupported@1.0.0",
            "1.28.0");
        Assert.Throws<NotSupportedException>(() => _subject.Write(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("wasi-command@0.2.10")]
    [InlineData("wasi-command@0.2.11")]
    [InlineData("netwasm:runtime/process@1.0.0")]
    public void RejectsUnsupportedExecutionContract(string contractKey)
    {
        var request = new CanonicalComponentAdapterRequest(
            contractKey,
            CanonicalComponentAdapterWriter.SupportedJcoVersion);
        Assert.Throws<NotSupportedException>(() => _subject.Write(request));
    }

    [Fact]
    public void RejectsNullRequest() => Assert.Throws<ArgumentNullException>(() => _subject.Write(null!));
}
