using System.Collections.Immutable;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmArtifactClientTests
{
    [Fact]
    public async Task ResolvesAndValidatesTheArtifactBeforeLaunchingWithExactCallerChannels()
    {
        var events = new List<string>();
        var source = Path.GetFullPath("Example.Tests.dll");
        var paths = new NetWasmArtifactPaths(
            source,
            Path.GetFullPath("Example.Tests.netwasm.execution.json"),
            Path.GetFullPath("Example.Tests.netwasm.request.json"));
        var descriptor = DescriptorFixture.Create();
        var baseRequest = ExecutionRequestFixture.Create();
        var expectedResult = ExecutionResultFixture.Normal(42);
        var arguments = ImmutableArray.Create("--list", "", "spaced value");
        await using var standardOutput = new MemoryStream();
        await using var standardError = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var subject = new NetWasmArtifactClient(
            new ArtifactPathResolverStub(value =>
            {
                Assert.Equal(source, value);
                events.Add("paths");
                return paths;
            }),
            new ArtifactDocumentReaderStub((path, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                if (path == paths.DescriptorPath)
                {
                    events.Add("descriptor-document");
                    return new byte[] { 1 };
                }
                Assert.Equal(paths.RequestPath, path);
                events.Add("request-document");
                return new byte[] { 2 };
            }),
            new ExecutionDescriptorReaderStub(bytes =>
            {
                Assert.Equal(1, bytes.Span[0]);
                events.Add("descriptor");
                return descriptor;
            }),
            new ExecutionRequestReaderStub(bytes =>
            {
                Assert.Equal(2, bytes.Span[0]);
                events.Add("request");
                return baseRequest;
            }),
            new ArtifactLauncherStub((actualDescriptor, actualPaths, actualArguments, stdout, stderr, token) =>
            {
                Assert.Same(descriptor, actualDescriptor);
                Assert.Same(paths, actualPaths);
                Assert.Equal<string>(arguments, actualArguments);
                Assert.Same(standardOutput, stdout);
                Assert.Same(standardError, stderr);
                Assert.Equal(cancellation.Token, token);
                events.Add("launch");
                return ValueTask.FromResult(expectedResult);
            }));

        var result = await subject.ExecuteAsync(
            new NetWasmArtifactExecutionRequest(source, arguments),
            standardOutput,
            standardError,
            cancellation.Token);

        Assert.Same(expectedResult, result);
        Assert.Equal(
            ["paths", "descriptor-document", "request-document", "descriptor", "request", "launch"],
            events);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("manifest")]
    public async Task RejectsAStaleOrMismatchedRequestBeforeLaunching(string mismatch)
    {
        var descriptor = DescriptorFixture.Create();
        var baseRequest = ExecutionRequestFixture.Create();
        baseRequest = mismatch == "fingerprint"
            ? baseRequest with { BuildFingerprint = new string('f', 64) }
            : baseRequest with { DeploymentManifestSha256 = new string('f', 64) };
        var subject = CreateSubject(
            descriptor,
            baseRequest,
            new ArtifactLauncherStub((_, _, _, _, _, _) =>
                throw new Xunit.Sdk.XunitException("Must not launch mismatched build channels.")));

        await Assert.ThrowsAsync<InvalidDataException>(async () => await subject.ExecuteAsync(
            Request(),
            new MemoryStream(),
            new MemoryStream()));
    }

    [Fact]
    public async Task PropagatesCallerCancellationToTheLauncher()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var subject = CreateSubject(
            DescriptorFixture.Create(),
            ExecutionRequestFixture.Create(),
            new ArtifactLauncherStub((_, _, _, _, _, token) =>
                ValueTask.FromException<NetWasmExecutionResult>(new OperationCanceledException(token))));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await subject.ExecuteAsync(
                Request(),
                new MemoryStream(),
                new MemoryStream(),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task RejectsDefaultOrInvalidArgumentsBeforeReadingArtifactDocuments()
    {
        var subject = new NetWasmArtifactClient(
            new ArtifactPathResolverStub(_ => throw new Xunit.Sdk.XunitException("Must not resolve.")),
            new ArtifactDocumentReaderStub((_, _) => throw new Xunit.Sdk.XunitException("Must not read.")),
            new ExecutionDescriptorReaderStub(_ => throw new Xunit.Sdk.XunitException("Must not read.")),
            new ExecutionRequestReaderStub(_ => throw new Xunit.Sdk.XunitException("Must not read.")),
            new ArtifactLauncherStub((_, _, _, _, _, _) => throw new Xunit.Sdk.XunitException("Must not launch.")));

        await Assert.ThrowsAsync<ArgumentException>(async () => await subject.ExecuteAsync(
            new NetWasmArtifactExecutionRequest(Path.GetFullPath("source.dll"), default),
            new MemoryStream(),
            new MemoryStream()));
        await Assert.ThrowsAsync<ArgumentException>(async () => await subject.ExecuteAsync(
            new NetWasmArtifactExecutionRequest(Path.GetFullPath("source.dll"), ["bad\0argument"]),
            new MemoryStream(),
            new MemoryStream()));
    }

    [Theory]
    [InlineData("paths")]
    [InlineData("documents")]
    [InlineData("descriptors")]
    [InlineData("requests")]
    [InlineData("launcher")]
    public void RejectsMissingDependencies(string dependency)
    {
        var paths = dependency == "paths" ? null : new ArtifactPathResolverStub(_ => throw new NotImplementedException());
        var documents = dependency == "documents" ? null : new ArtifactDocumentReaderStub((_, _) => throw new NotImplementedException());
        var descriptors = dependency == "descriptors" ? null : new ExecutionDescriptorReaderStub(_ => throw new NotImplementedException());
        var requests = dependency == "requests" ? null : new ExecutionRequestReaderStub(_ => throw new NotImplementedException());
        var launcher = dependency == "launcher" ? null : new ArtifactLauncherStub((_, _, _, _, _, _) => throw new NotImplementedException());

        Assert.Throws<ArgumentNullException>(() => new NetWasmArtifactClient(
            paths!, documents!, descriptors!, requests!, launcher!));
    }

    private static NetWasmArtifactClient CreateSubject(
        ExecutionDescriptor descriptor,
        NetWasmExecutionRequest request,
        INetWasmArtifactLauncher launcher)
    {
        var paths = new NetWasmArtifactPaths(
            Path.GetFullPath("source.dll"),
            Path.GetFullPath("source.netwasm.execution.json"),
            Path.GetFullPath("source.netwasm.request.json"));
        return new NetWasmArtifactClient(
            new ArtifactPathResolverStub(_ => paths),
            new ArtifactDocumentReaderStub((path, _) =>
                path == paths.DescriptorPath ? new byte[] { 1 } : new byte[] { 2 }),
            new ExecutionDescriptorReaderStub(_ => descriptor),
            new ExecutionRequestReaderStub(_ => request),
            launcher);
    }

    private static NetWasmArtifactExecutionRequest Request() => new(
        Path.GetFullPath("source.dll"),
        []);
}

internal sealed class ArtifactPathResolverStub(Func<string, NetWasmArtifactPaths> resolve)
    : INetWasmArtifactPathResolver
{
    public NetWasmArtifactPaths Resolve(string sourcePath) => resolve(sourcePath);
}

internal sealed class ArtifactDocumentReaderStub(
    Func<string, CancellationToken, ReadOnlyMemory<byte>> read) : IArtifactDocumentReader
{
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(read(path, cancellationToken));
}

internal sealed class ExecutionDescriptorReaderStub(
    Func<ReadOnlyMemory<byte>, ExecutionDescriptor> read) : IExecutionDescriptorReader
{
    public ExecutionDescriptor Read(ReadOnlyMemory<byte> utf8Json) => read(utf8Json);
}

internal sealed class ExecutionRequestReaderStub(
    Func<ReadOnlyMemory<byte>, NetWasmExecutionRequest> read) : INetWasmExecutionRequestReader
{
    public NetWasmExecutionRequest Read(ReadOnlyMemory<byte> utf8Json) => read(utf8Json);
}

internal sealed class ArtifactLauncherStub(
    Func<ExecutionDescriptor, NetWasmArtifactPaths, ImmutableArray<string>, Stream, Stream,
        CancellationToken, ValueTask<NetWasmExecutionResult>> execute) : INetWasmArtifactLauncher
{
    public ValueTask<NetWasmExecutionResult> ExecuteAsync(
        ExecutionDescriptor descriptor,
        NetWasmArtifactPaths paths,
        ImmutableArray<string> arguments,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken) =>
        execute(descriptor, paths, arguments, standardOutput, standardError, cancellationToken);
}
