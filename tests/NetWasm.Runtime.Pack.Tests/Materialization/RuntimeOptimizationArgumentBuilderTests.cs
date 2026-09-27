using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeOptimizationArgumentBuilderTests
{
    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm64", true)]
    public void ReproducesPinnedEmscriptenSizeOptimization(
        string target,
        bool memory64)
    {
        using var directory = new TemporaryDirectory();
        var output = directory.PathTo("runtime.wasm");

        var arguments = new RuntimeOptimizationArgumentBuilder().Build(new(
            RuntimePackTestData.Target(target), output));

        Assert.Contains("--post-emscripten", arguments);
        Assert.Contains("-Oz", arguments);
        Assert.Contains("--strip-debug", arguments);
        Assert.Contains("--strip-producers", arguments);
        Assert.Equal(2, arguments.Count(argument => argument == Path.GetFullPath(output)));
        Assert.Equal(memory64, arguments.Contains("--enable-memory64"));
    }

    [Fact]
    public void RejectsMissingRequest() =>
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeOptimizationArgumentBuilder().Build(null!));
}
