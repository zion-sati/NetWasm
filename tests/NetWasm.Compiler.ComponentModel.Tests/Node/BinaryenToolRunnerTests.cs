using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Tests.Node;

public sealed class BinaryenToolRunnerTests
{
    private static readonly string NodePath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "node"));
    private static readonly string WasmOptPath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "binaryen", "bin", "wasm-opt"));
    private static readonly string WasmMergePath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "binaryen", "bin", "wasm-merge"));

    [Theory]
    [InlineData(BinaryenToolIds.WasmOpt)]
    [InlineData(BinaryenToolIds.WasmMerge)]
    public void RunSelectsExactScriptAndReturnsUnchangedResult(string toolId)
    {
        var expected = new ToolResult(7, "output", "error");
        var node = new RecordingNodeCommandRunner(expected);
        var runner = CreateRunner(node);
        var arguments = ImmutableArray.Create("input.wasm", "--output", "output.wasm");

        var result = runner.Run(toolId, arguments);

        Assert.Same(expected, result);
        Assert.Equal(NodePath, node.Command!.NodePath);
        Assert.Equal(toolId == BinaryenToolIds.WasmOpt ? WasmOptPath : WasmMergePath,
            node.Command.ScriptPath);
        Assert.Equal(arguments.ToArray(), node.Command.Arguments.ToArray());
        Assert.Equal(1, node.CallCount);
    }

    [Theory]
    [InlineData(BinaryenToolIds.WasmOpt)]
    [InlineData(BinaryenToolIds.WasmMerge)]
    public void RunSelectsConfiguredNativeToolWithoutPortableRetry(string toolId)
    {
        var expected = new ToolResult(9, "", "native failure");
        var node = new RecordingNodeCommandRunner(new(0, "portable", ""));
        var processes = new RecordingExternalToolRunner(expected);
        var configuration = CreateConfiguration() with
        {
            NativeTools = [new(toolId,
                toolId == BinaryenToolIds.WasmOpt ? WasmOptPath : WasmMergePath)],
        };
        var runner = new BinaryenToolRunner(configuration, node, processes);
        var arguments = ImmutableArray.Create("input.wasm", "--output", "output.wasm");

        var result = runner.Run(toolId, arguments);

        Assert.Same(expected, result);
        Assert.Equal(
            toolId == BinaryenToolIds.WasmOpt ? WasmOptPath : WasmMergePath,
            processes.Executable);
        Assert.Equal(arguments, processes.Arguments);
        Assert.Equal(1, processes.CallCount);
        Assert.Equal(0, node.CallCount);
    }

    [Fact]
    public void ConstructorRejectsNullInputs()
    {
        var node = new RecordingNodeCommandRunner(new(0, "", ""));

        Assert.Throws<ArgumentNullException>(() => new BinaryenToolRunner(null!, node));
        Assert.Throws<ArgumentNullException>(() => new BinaryenToolRunner(
            CreateConfiguration(), null!));
        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with
            {
                NativeTools = [new(BinaryenToolIds.WasmOpt, WasmOptPath)],
            }, node, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("node")]
    public void ConstructorRejectsInvalidNodePath(string nodePath)
    {
        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with { NodePath = nodePath },
            new RecordingNodeCommandRunner(new(0, "", ""))));
    }

    [Fact]
    public void ConstructorRejectsMissingScripts()
    {
        var node = new RecordingNodeCommandRunner(new(0, "", ""));

        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with { Scripts = default }, node));
        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with { Scripts = [] }, node));
        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with { Scripts = [CreateScript(BinaryenToolIds.WasmOpt)] },
            node));
    }

    [Fact]
    public void ConstructorRejectsNullDuplicateAndUnknownScripts()
    {
        var node = new RecordingNodeCommandRunner(new(0, "", ""));
        var duplicate = CreateScript(BinaryenToolIds.WasmOpt);

        Assert.Throws<ArgumentNullException>(() => new BinaryenToolRunner(
            CreateConfiguration() with
            {
                Scripts = [null!, CreateScript(BinaryenToolIds.WasmMerge)],
            }, node));
        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with
            {
                Scripts = [duplicate, duplicate],
            }, node));
        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with
            {
                Scripts = [CreateScript("unknown"), CreateScript(BinaryenToolIds.WasmMerge)],
            }, node));
    }

    [Theory]
    [InlineData("", "/script")]
    [InlineData(BinaryenToolIds.WasmOpt, "")]
    [InlineData(BinaryenToolIds.WasmOpt, "bin/wasm-opt")]
    public void ConstructorRejectsInvalidScriptIdentityOrPath(
        string toolId,
        string scriptPath)
    {
        var scripts = ImmutableArray.Create(
            new BinaryenToolScript(toolId, scriptPath),
            CreateScript(BinaryenToolIds.WasmMerge));

        Assert.Throws<ArgumentException>(() => new BinaryenToolRunner(
            CreateConfiguration() with { Scripts = scripts },
            new RecordingNodeCommandRunner(new(0, "", ""))));
    }

    [Fact]
    public void RunRejectsInvalidInputsBeforeNodeInvocation()
    {
        var node = new RecordingNodeCommandRunner(new(0, "", ""));
        var runner = CreateRunner(node);

        Assert.Throws<ArgumentException>(() => runner.Run("", []));
        Assert.Throws<ArgumentException>(() => runner.Run(
            BinaryenToolIds.WasmOpt, default));
        Assert.Throws<ArgumentNullException>(() => runner.Run(
            BinaryenToolIds.WasmOpt, [null!]));
        var exception = Assert.Throws<UnsupportedBinaryenToolException>(
            () => runner.Run("unknown", []));
        Assert.Equal("unknown", exception.ToolId);
        Assert.Equal(0, node.CallCount);
    }

    [Fact]
    public void RunRejectsNullNodeResult()
    {
        var runner = CreateRunner(new RecordingNodeCommandRunner(null));

        Assert.Throws<InvalidOperationException>(() => runner.Run(
            BinaryenToolIds.WasmOpt, []));
    }

    private static BinaryenToolRunner CreateRunner(ISystemNodeCommandRunner node) =>
        new(CreateConfiguration(), node);

    private static BinaryenToolRunnerConfiguration CreateConfiguration() => new(
        NodePath,
        [
            CreateScript(BinaryenToolIds.WasmOpt),
            CreateScript(BinaryenToolIds.WasmMerge),
        ]);

    private static BinaryenToolScript CreateScript(string toolId) => new(
        toolId,
        toolId == BinaryenToolIds.WasmOpt ? WasmOptPath : WasmMergePath);

    private sealed class RecordingNodeCommandRunner(ToolResult? result) :
        ISystemNodeCommandRunner
    {
        public int CallCount { get; private set; }

        public SystemNodeCommand? Command { get; private set; }

        public ToolResult Run(SystemNodeCommand command)
        {
            CallCount++;
            Command = command;
            return result!;
        }
    }

    private sealed class RecordingExternalToolRunner(ToolResult result) :
        IExternalToolRunner
    {
        public string? Executable { get; private set; }
        public ImmutableArray<string> Arguments { get; private set; }
        public int CallCount { get; private set; }

        public ToolResult Run(string executable, IEnumerable<string> arguments)
        {
            Executable = executable;
            Arguments = [.. arguments];
            CallCount++;
            return result;
        }
    }
}
