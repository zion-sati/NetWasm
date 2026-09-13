using System.Collections.Immutable;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ProcessNetWasmArtifactLauncherTests
{
    [Fact]
    public async Task LaunchesOnlyTheDescriptorSelectedHostAndReadsItsStructuredResult()
    {
        var descriptor = DescriptorFixture.Create();
        var paths = Paths();
        var arguments = ImmutableArray.Create("--list", "", "spaced value", "quoted\"value");
        var expected = ExecutionResultFixture.Normal(42);
        var resultBytes = new NetWasmExecutionResultWriter(new NetWasmExecutionResultValidator())
            .Write(expected);
        var channel = new TemporaryResultChannelStub(Path.GetFullPath("result channel.json"), resultBytes);
        await using var standardOutput = new MemoryStream();
        await using var standardError = new MemoryStream();
        ArtifactProcessStart? captured = null;
        var subject = new ProcessNetWasmArtifactLauncher(
            new ArtifactProcessRunnerStub((start, stdout, stderr, token) =>
            {
                captured = start;
                Assert.Same(standardOutput, stdout);
                Assert.Same(standardError, stderr);
                Assert.False(token.IsCancellationRequested);
                return ValueTask.FromResult(0);
            }),
            new TemporaryResultChannelFactoryStub(() => channel),
            new NetWasmExecutionResultReader(new NetWasmExecutionResultValidator()));

        var result = await subject.ExecuteAsync(
            descriptor,
            paths,
            arguments,
            standardOutput,
            standardError,
            CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(descriptor.HostExecutablePath, captured!.FileName);
        Assert.Equal(Path.GetDirectoryName(paths.SourcePath), captured.WorkingDirectory);
        Assert.Equal<string>(
            [
                descriptor.LauncherPath,
                "--descriptor", paths.DescriptorPath,
                "--request", paths.RequestPath,
                "--result", channel.Path,
                "--",
                "--list", "", "spaced value", "quoted\"value",
            ],
            captured.Arguments);
        Assert.Equal(1, channel.ReadCount);
        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task RejectsProcessFailureWithoutAcceptingAResultChannel()
    {
        var channel = new TemporaryResultChannelStub(
            Path.GetFullPath("result.json"),
            new byte[] { 1 });
        var subject = new ProcessNetWasmArtifactLauncher(
            new ArtifactProcessRunnerStub((_, _, _, _) => ValueTask.FromResult(1)),
            new TemporaryResultChannelFactoryStub(() => channel),
            new ExecutionResultReaderStub(_ => throw new Xunit.Sdk.XunitException("Must not parse a failed transport.")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await subject.ExecuteAsync(
            DescriptorFixture.Create(),
            Paths(),
            [],
            new MemoryStream(),
            new MemoryStream(),
            CancellationToken.None));

        Assert.Equal(0, channel.ReadCount);
        Assert.Equal(1, channel.DisposeCount);
    }

    [Fact]
    public async Task PropagatesCancellationAndStillReleasesThePrivateResultChannel()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var channel = new TemporaryResultChannelStub(
            Path.GetFullPath("result.json"),
            ReadOnlyMemory<byte>.Empty);
        var subject = new ProcessNetWasmArtifactLauncher(
            new ArtifactProcessRunnerStub((_, _, _, token) =>
                ValueTask.FromException<int>(new OperationCanceledException(token))),
            new TemporaryResultChannelFactoryStub(() => channel),
            new ExecutionResultReaderStub(_ => throw new Xunit.Sdk.XunitException("Must not parse cancellation.")));

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await subject.ExecuteAsync(
            DescriptorFixture.Create(),
            Paths(),
            [],
            new MemoryStream(),
            new MemoryStream(),
            cancellation.Token));

        Assert.Equal(0, channel.ReadCount);
        Assert.Equal(1, channel.DisposeCount);
    }

    [Theory]
    [InlineData("processes")]
    [InlineData("channels")]
    [InlineData("results")]
    public void RejectsMissingDependencies(string dependency)
    {
        var processes = dependency == "processes" ? null : new ArtifactProcessRunnerStub((_, _, _, _) => throw new NotImplementedException());
        var channels = dependency == "channels" ? null : new TemporaryResultChannelFactoryStub(() => throw new NotImplementedException());
        var results = dependency == "results" ? null : new ExecutionResultReaderStub(_ => throw new NotImplementedException());

        Assert.Throws<ArgumentNullException>(() => new ProcessNetWasmArtifactLauncher(
            processes!, channels!, results!));
    }

    private static NetWasmArtifactPaths Paths() => new(
        Path.GetFullPath("Example.Tests.dll"),
        Path.GetFullPath("Example.Tests.netwasm.execution.json"),
        Path.GetFullPath("Example.Tests.netwasm.request.json"));
}

internal sealed class ArtifactProcessRunnerStub(
    Func<ArtifactProcessStart, Stream, Stream, CancellationToken, ValueTask<int>> run)
    : IArtifactProcessRunner
{
    public ValueTask<int> RunAsync(
        ArtifactProcessStart start,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken) =>
        run(start, standardOutput, standardError, cancellationToken);
}

internal sealed class TemporaryResultChannelFactoryStub(Func<ITemporaryResultChannel> create)
    : ITemporaryResultChannelFactory
{
    public ITemporaryResultChannel Create() => create();
}

internal sealed class TemporaryResultChannelStub(string path, ReadOnlyMemory<byte> bytes)
    : ITemporaryResultChannel
{
    public string Path { get; } = path;
    internal int ReadCount { get; private set; }
    internal int DisposeCount { get; private set; }

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return ValueTask.FromResult(bytes);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class ExecutionResultReaderStub(
    Func<ReadOnlyMemory<byte>, NetWasmExecutionResult> read) : INetWasmExecutionResultReader
{
    public NetWasmExecutionResult Read(ReadOnlyMemory<byte> utf8Json) => read(utf8Json);
}
