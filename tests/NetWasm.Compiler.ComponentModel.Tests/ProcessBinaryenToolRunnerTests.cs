using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ProcessBinaryenToolRunnerTests
{
    [Theory]
    [InlineData(BinaryenToolIds.WasmOpt)]
    [InlineData(BinaryenToolIds.WasmMerge)]
    public void RunDelegatesKnownToolAndExplicitArguments(string toolId)
    {
        var process = new RecordingExternalToolRunner();
        var runner = AsRunner(new ProcessBinaryenToolRunner(process));
        var arguments = ImmutableArray.Create("input.wasm", "--output", "output.wasm");

        var result = runner.Run(toolId, arguments);

        Assert.Same(process.Result, result);
        Assert.Equal(toolId, process.Executable);
        Assert.Equal(arguments, process.Arguments);
        Assert.Equal(1, process.CallCount);
    }

    [Fact]
    public void ConstructorRejectsMissingProcessRunner()
    {
        Assert.Throws<ArgumentNullException>(() => new ProcessBinaryenToolRunner(null!));
    }

    [Fact]
    public void RunRejectsInvalidRequestsBeforeProcessInvocation()
    {
        var process = new RecordingExternalToolRunner();
        var runner = AsRunner(new ProcessBinaryenToolRunner(process));

        Assert.Throws<ArgumentException>(() => runner.Run("", []));
        var exception = Assert.Throws<UnsupportedBinaryenToolException>(
            () => runner.Run("unknown", []));
        Assert.Equal("unknown", exception.ToolId);
        Assert.Throws<ArgumentException>(() => runner.Run(BinaryenToolIds.WasmOpt, default));
        Assert.Throws<ArgumentNullException>(() =>
            runner.Run(BinaryenToolIds.WasmOpt, [null!]));
        Assert.Equal(0, process.CallCount);
    }

    private static IBinaryenToolRunner AsRunner(object runner) =>
        (IBinaryenToolRunner)runner;

    private sealed class RecordingExternalToolRunner : IExternalToolRunner
    {
        public ToolResult Result { get; } = new(7, "output", "error");
        public string? Executable { get; private set; }
        public ImmutableArray<string> Arguments { get; private set; }
        public int CallCount { get; private set; }

        public ToolResult Run(string executable, IEnumerable<string> arguments)
        {
            CallCount++;
            Executable = executable;
            Arguments = [.. arguments];
            return Result;
        }
    }
}
