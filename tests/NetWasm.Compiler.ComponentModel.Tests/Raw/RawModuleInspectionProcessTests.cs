using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleInspectionProcessTests
{
    private static readonly string NodePath = Path.Combine(Path.GetTempPath(), "node");
    private static readonly string ScriptPath = Path.Combine(Path.GetTempPath(), "inspect.mjs");
    private static readonly string ModulePath = Path.Combine(Path.GetTempPath(), "application.wasm");
    private static readonly string BinaryenPath = Path.Combine(Path.GetTempPath(), "binaryen", "index.js");

    [Fact]
    public void RunInvokesTheVerifiedCommandWithExactInspectionArguments()
    {
        var node = new RecordingNodeCommandRunner();
        var process = Assert.IsAssignableFrom<IRawModuleInspectionProcess>(
            new RawModuleInspectionProcess(node));
        var request = new RawModuleInspectionRequest(
            NodePath, ScriptPath, ModulePath, BinaryenPath);

        var result = process.Run(request);

        Assert.Same(node.Result, result);
        Assert.Equal(NodePath, node.Command!.NodePath);
        Assert.Equal(ScriptPath, node.Command.ScriptPath);
        Assert.Equal([ModulePath, BinaryenPath], node.Command.Arguments);
    }

    [Fact]
    public void ConstructorAndRunRejectMissingInputsBeforeNodeInvocation()
    {
        var node = new RecordingNodeCommandRunner();
        var process = new RawModuleInspectionProcess(node);

        Assert.Throws<ArgumentNullException>(() => new RawModuleInspectionProcess(null!));
        Assert.Throws<ArgumentNullException>(() => process.Run(null!));
        Assert.Null(node.Command);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(true, "module.wasm")]
    [InlineData(false, " ")]
    [InlineData(false, "binaryen/index.js")]
    public void RunRequiresAbsoluteModuleAndBinaryenPaths(
        bool modulePath,
        string invalidPath)
    {
        var node = new RecordingNodeCommandRunner();
        var process = new RawModuleInspectionProcess(node);
        var request = new RawModuleInspectionRequest(
            NodePath,
            ScriptPath,
            modulePath ? invalidPath : ModulePath,
            modulePath ? BinaryenPath : invalidPath);

        Assert.Throws<ArgumentException>(() => process.Run(request));

        Assert.Null(node.Command);
    }

    private sealed class RecordingNodeCommandRunner : ISystemNodeCommandRunner
    {
        public ToolResult Result { get; } = new(0, "result", "evidence");
        public SystemNodeCommand? Command { get; private set; }

        public ToolResult Run(SystemNodeCommand command)
        {
            Command = command;
            return Result;
        }
    }
}
