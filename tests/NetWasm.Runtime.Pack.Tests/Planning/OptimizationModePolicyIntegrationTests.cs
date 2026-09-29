using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Planning;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Planning;

public sealed class OptimizationModePolicyIntegrationTests
{
    [Theory]
    [InlineData(RuntimeWasmOptimization.None, FinalWasmOptimization.None, null)]
    [InlineData(RuntimeWasmOptimization.O0, FinalWasmOptimization.O0, "-O0")]
    [InlineData(RuntimeWasmOptimization.O1, FinalWasmOptimization.O1, "-O1")]
    [InlineData(RuntimeWasmOptimization.O2, FinalWasmOptimization.O2, "-O2")]
    [InlineData(RuntimeWasmOptimization.O3, FinalWasmOptimization.O3, "-O3")]
    [InlineData(RuntimeWasmOptimization.Os, FinalWasmOptimization.Os, "-Os")]
    [InlineData(RuntimeWasmOptimization.Oz, FinalWasmOptimization.Oz, "-Oz")]
    public void RuntimeAndFinalPassesUseTheSameCanonicalMode(
        RuntimeWasmOptimization runtimeOptimization,
        FinalWasmOptimization finalOptimization,
        string? expectedFlag)
    {
        using var directory = new TemporaryDirectory();
        var runtimeOutput = directory.PathTo("runtime.wasm");
        var runtimeArguments = new RuntimeOptimizationArgumentBuilder().Build(new(
            RuntimePackTestData.Target("wasm32"), runtimeOutput, runtimeOptimization));
        var input = directory.PathTo("input.wasm");
        var output = directory.PathTo("output.wasm");
        File.WriteAllBytes(input, [0x2a]);
        var tools = new RecordingBinaryenTools();

        new ComponentCoreModuleOptimizer(
            tools,
            new SystemFileExistence(),
            new RecordingValidator(),
            new SystemFileCopier()).Optimize(
                input,
                output,
                ComponentTarget.Wasm32Wasi02,
                finalOptimization);

        if (expectedFlag is null)
        {
            Assert.Empty(runtimeArguments);
            Assert.Empty(tools.Arguments);
            Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
            return;
        }

        Assert.Equal(expectedFlag, Assert.Single(runtimeArguments, IsOptimizationFlag));
        Assert.Equal(expectedFlag, Assert.Single(tools.Arguments, IsOptimizationFlag));
    }

    private static bool IsOptimizationFlag(string argument) =>
        argument is "-O0" or "-O1" or "-O2" or "-O3" or "-Os" or "-Oz";

    private sealed class RecordingBinaryenTools : IBinaryenToolRunner
    {
        public ImmutableArray<string> Arguments { get; private set; } = [];

        public ToolResult Run(string toolId, ImmutableArray<string> arguments)
        {
            Assert.Equal(BinaryenToolIds.WasmOpt, toolId);
            Arguments = arguments;
            return new(0, string.Empty, string.Empty);
        }
    }

    private sealed class RecordingValidator : IWasmCoreModuleValidator
    {
        public void Validate(string path) => Assert.True(File.Exists(path));
    }
}
