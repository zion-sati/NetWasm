using System.Collections.Immutable;
using System.Text;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ArtifactProcessRunnerTests
{
    [Fact]
    public async Task ExecutesWithoutAShellAndCopiesBothProcessStreams()
    {
        var dotnet = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(dotnet));
        await using var standardOutput = new MemoryStream();
        await using var standardError = new MemoryStream();

        var exitCode = await new ArtifactProcessRunner().RunAsync(
            new ArtifactProcessStart(
                dotnet,
                ImmutableArray.Create("--version"),
                Path.GetTempPath()),
            standardOutput,
            standardError,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(Encoding.UTF8.GetString(standardOutput.ToArray()).Trim());
        Assert.Empty(standardError.ToArray());
    }

    [Fact]
    public async Task PreCancellationPreventsProcessLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new ArtifactProcessRunner().RunAsync(
                new ArtifactProcessStart(
                    Path.GetFullPath("missing-host"),
                    [],
                    Path.GetTempPath()),
                new MemoryStream(),
                new MemoryStream(),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task RejectsASharedOutputChannelBeforeProcessLaunch()
    {
        await using var output = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await new ArtifactProcessRunner().RunAsync(
                new ArtifactProcessStart(
                    Path.GetFullPath("missing-host"),
                    [],
                    Path.GetTempPath()),
                output,
                output,
                CancellationToken.None));
    }
}
