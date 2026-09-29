using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeOptimizationArgumentBuilderTests
{
    [Theory]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O0, "-O0")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O1, "-O1")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O2, "-O2")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.O3, "-O3")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.Os, "-Os")]
    [InlineData("wasm32", false, RuntimeWasmOptimization.Oz, "-Oz")]
    [InlineData("wasm64", true, RuntimeWasmOptimization.Oz, "-Oz")]
    public void ReproducesPinnedEmscriptenOptimizationWithSelectedMode(
        string target,
        bool memory64,
        RuntimeWasmOptimization optimization,
        string optimizationFlag)
    {
        using var directory = new TemporaryDirectory();
        var output = directory.PathTo("runtime.wasm");

        var arguments = new RuntimeOptimizationArgumentBuilder().Build(new(
            RuntimePackTestData.Target(target), output, optimization));

        Assert.Contains("--post-emscripten", arguments);
        Assert.Equal(optimizationFlag, Assert.Single(arguments, IsOptimizationFlag));
        Assert.Contains("--strip-debug", arguments);
        Assert.Contains("--strip-producers", arguments);
        Assert.Equal(2, arguments.Count(argument => argument == Path.GetFullPath(output)));
        Assert.Equal(memory64, arguments.Contains("--enable-memory64"));
    }

    [Fact]
    public void NoneProducesNoOptimizerArguments()
    {
        using var directory = new TemporaryDirectory();
        Assert.Empty(new RuntimeOptimizationArgumentBuilder().Build(new(
            RuntimePackTestData.Target("wasm32"), directory.PathTo("runtime.wasm"),
            RuntimeWasmOptimization.None)));
    }

    [Fact]
    public void RejectsUndefinedOptimization() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeOptimizationArgumentBuilder().Build(new(
                RuntimePackTestData.Target("wasm32"), "runtime.wasm",
                (RuntimeWasmOptimization)42)));

    [Fact]
    public void RejectsMissingRequest() =>
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeOptimizationArgumentBuilder().Build(null!));

    private static bool IsOptimizationFlag(string argument) =>
        argument is "-O0" or "-O1" or "-O2" or "-O3" or "-Os" or "-Oz";
}
