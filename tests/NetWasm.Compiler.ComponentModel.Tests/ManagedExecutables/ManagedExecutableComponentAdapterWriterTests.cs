using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class ManagedExecutableComponentAdapterWriterTests
{
    [Theory]
    [InlineData(ManagedExecutableCompletionShape.Synchronous)]
    [InlineData(ManagedExecutableCompletionShape.Asynchronous)]
    public void DelegatesTheUnchangedRequestToExactlyOneRegisteredWriter(
        ManagedExecutableCompletionShape shape)
    {
        var synchronous = new RecordingWriter();
        var asynchronous = new RecordingWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(new ManagedExecutableComponentAdapterWriter(
        [
            new(ManagedExecutableCompletionShape.Synchronous, synchronous),
            new(ManagedExecutableCompletionShape.Asynchronous, asynchronous),
        ]));
        var request = Request(shape);

        writer.Write(request);

        Assert.Same(request, shape == ManagedExecutableCompletionShape.Synchronous
            ? synchronous.Request : asynchronous.Request);
        Assert.Null(shape == ManagedExecutableCompletionShape.Synchronous
            ? asynchronous.Request : synchronous.Request);
    }

    [Fact]
    public void RejectsIncompleteAmbiguousOrInvalidRegistrations()
    {
        var downstream = new RecordingWriter();
        Assert.Throws<ArgumentNullException>(() => new ManagedExecutableComponentAdapterWriter(null!));
        Assert.Throws<ArgumentException>(() => new ManagedExecutableComponentAdapterWriter([]));
        Assert.Throws<ArgumentNullException>(() => new ManagedExecutableComponentAdapterWriter(
            [new(ManagedExecutableCompletionShape.Synchronous, null!)]));
        Assert.Throws<ArgumentException>(() => new ManagedExecutableComponentAdapterWriter(
            [new((ManagedExecutableCompletionShape)99, downstream)]));
        Assert.Throws<ArgumentException>(() => new ManagedExecutableComponentAdapterWriter(
        [
            new(ManagedExecutableCompletionShape.Synchronous, downstream),
            new(ManagedExecutableCompletionShape.Synchronous, downstream),
        ]));
        Assert.Null(downstream.Request);
    }

    [Fact]
    public void RejectsInvalidRequestsBeforeDelegation()
    {
        var downstream = new RecordingWriter();
        var writer = Assert.IsAssignableFrom<IManagedExecutableComponentAdapterWriter>(new ManagedExecutableComponentAdapterWriter(
        [
            new(ManagedExecutableCompletionShape.Synchronous, downstream),
            new(ManagedExecutableCompletionShape.Asynchronous, downstream),
        ]));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentNullException>(() => writer.Write(
            Request(ManagedExecutableCompletionShape.Synchronous) with { EntryPoint = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(Request((ManagedExecutableCompletionShape)99)));
        Assert.Null(downstream.Request);
    }

    private static ManagedExecutableComponentAdapterRequest Request(ManagedExecutableCompletionShape shape) =>
        new("output.wasm", ComponentTarget.Wasm32Wasi02,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.Void, shape));

    private sealed class RecordingWriter : IManagedExecutableComponentAdapterWriter
    {
        public ManagedExecutableComponentAdapterRequest? Request { get; private set; }
        public void Write(ManagedExecutableComponentAdapterRequest request) => Request = request;
    }
}
