using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ComponentCoreModuleOptimizerTests
{
    [Theory]
    [InlineData("wasm32", false)]
    [InlineData("wasm64", true)]
    public void RunsFinalLtoWithTargetFeatures(string width, bool memory64)
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0);
        var output = files.PathFor("output.wasm");
        var tools = new RecordingTools();
        var target = width == "wasm64"
            ? ComponentTarget.Wasm64Wasi02
            : ComponentTarget.Wasm32Wasi02;

        new ComponentCoreModuleOptimizer(tools, new SystemFileExistence())
            .Optimize(input, output, target);

        Assert.Equal(BinaryenToolIds.WasmOpt, tools.ToolId);
        Assert.Contains("-Oz", tools.Arguments);
        Assert.Contains("--remove-unused-module-elements", tools.Arguments);
        Assert.Equal(memory64, tools.Arguments.Contains("--enable-memory64"));
        Assert.Equal(["--output", output], tools.Arguments[^2..]);
    }

    [Fact]
    public void ReportsOptimizerFailure()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0);
        var tools = new RecordingTools { Result = new(1, "", "bad module\n") };

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentCoreModuleOptimizer(tools, new SystemFileExistence()).Optimize(
                input,
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("bad module", exception.Diagnostic.Message);
    }

    [Fact]
    public void ReportsUnknownOptimizerFailureWhenToolHasNoError()
    {
        using var files = new ComponentModelTestFiles();
        var input = files.Create("input.wasm", 0);
        var tools = new RecordingTools { Result = new(1, "", "") };

        var exception = Assert.Throws<CompilerException>(() =>
            new ComponentCoreModuleOptimizer(tools, new SystemFileExistence()).Optimize(
                input,
                files.PathFor("output.wasm"),
                ComponentTarget.Wasm32Wasi02));

        Assert.Contains("unknown error", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    private sealed class RecordingTools : IBinaryenToolRunner
    {
        public ToolResult Result { get; init; } = new(0, "", "");
        public string ToolId { get; private set; } = "";
        public string[] Arguments { get; private set; } = [];

        public ToolResult Run(string toolId, ImmutableArray<string> arguments)
        {
            ToolId = toolId;
            Arguments = arguments.ToArray();
            return Result;
        }
    }
}
