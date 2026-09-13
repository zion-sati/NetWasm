using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmArtifactPathResolverTests
{
    [Fact]
    public void ReplacesOnlyTheFinalSourceExtensionWithPublicExecutionSuffixes()
    {
        var source = Path.GetFullPath(Path.Combine("artifacts.with.dots", "Example.Tests.dll"));

        var paths = Assert.IsAssignableFrom<INetWasmArtifactPathResolver>(
            new NetWasmArtifactPathResolver()).Resolve(source);

        Assert.Equal(source, paths.SourcePath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("artifacts.with.dots", "Example.Tests.netwasm.execution.json")),
            paths.DescriptorPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("artifacts.with.dots", "Example.Tests.netwasm.request.json")),
            paths.RequestPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative.dll")]
    [InlineData("relative/../source.dll")]
    public void RejectsMissingRelativeOrNonCanonicalSources(string? source) =>
        Assert.ThrowsAny<ArgumentException>(() => new NetWasmArtifactPathResolver().Resolve(source!));

    [Fact]
    public void RejectsSourcesWithoutAFileExtension()
    {
        var source = Path.GetFullPath("source");

        Assert.Throws<ArgumentException>(() => new NetWasmArtifactPathResolver().Resolve(source));
    }
}
