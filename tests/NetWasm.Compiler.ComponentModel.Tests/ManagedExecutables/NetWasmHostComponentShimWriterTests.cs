using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

public sealed class NetWasmHostComponentShimWriterTests
{
    [Theory]
    [InlineData(ManagedExecutableCompletionShape.Synchronous)]
    [InlineData(ManagedExecutableCompletionShape.Asynchronous)]
    public void DelegatesTheUnchangedRequestToExactlyOneRegisteredWriter(
        ManagedExecutableCompletionShape shape)
    {
        var synchronous = new RecordingWriter();
        var asynchronous = new RecordingWriter();
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(new NetWasmHostComponentShimWriter(
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
        Assert.Throws<ArgumentNullException>(() => new NetWasmHostComponentShimWriter(null!));
        Assert.Throws<ArgumentException>(() => new NetWasmHostComponentShimWriter([]));
        Assert.Throws<ArgumentNullException>(() => new NetWasmHostComponentShimWriter(
            [new(ManagedExecutableCompletionShape.Synchronous, null!)]));
        Assert.Throws<ArgumentException>(() => new NetWasmHostComponentShimWriter(
            [new((ManagedExecutableCompletionShape)99, downstream)]));
        Assert.Throws<ArgumentException>(() => new NetWasmHostComponentShimWriter(
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
        var writer = Assert.IsAssignableFrom<INetWasmHostComponentShimWriter>(new NetWasmHostComponentShimWriter(
        [
            new(ManagedExecutableCompletionShape.Synchronous, downstream),
            new(ManagedExecutableCompletionShape.Asynchronous, downstream),
        ]));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write(Request((ManagedExecutableCompletionShape)99)));
        Assert.Null(downstream.Request);
    }

    private static NetWasmHostComponentShimRequest Request(ManagedExecutableCompletionShape shape) =>
        new("output.wasm", ComponentTarget.Wasm32Wasi02, shape);

    private sealed class RecordingWriter : INetWasmHostComponentShimWriter
    {
        public NetWasmHostComponentShimRequest? Request { get; private set; }
        public void Write(NetWasmHostComponentShimRequest request) => Request = request;
    }
}
