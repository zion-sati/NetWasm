using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class SystemNodeCommandRunnerTests
{
    private static readonly string NodePath = Path.Combine(Path.GetTempPath(), "node");
    private static readonly string ScriptPath = Path.Combine(Path.GetTempPath(), "script.mjs");

    [Fact]
    public void RunInvokesTheResolvedNodePathWithTheVerifiedPackageScript()
    {
        var tools = new RecordingConfiguredExternalToolRunner();
        var runner = Assert.IsAssignableFrom<ISystemNodeCommandRunner>(
            new SystemNodeCommandRunner(tools));

        var result = runner.Run(new(NodePath, ScriptPath, ["one", "two"]));

        Assert.Equal(new ToolResult(5, "result", string.Empty), result);
        Assert.Equal(NodePath, tools.Invocation!.Executable);
        Assert.Equal([ScriptPath, "one", "two"], tools.Invocation.Arguments);
        Assert.Equal(
            ["NODE_", "NPM_CONFIG_"],
            tools.Invocation.EnvironmentVariablePrefixesToRemove);
    }

    [Fact]
    public void ConstructorAndRunRejectMissingInputsBeforeToolExecution()
    {
        var tools = new RecordingConfiguredExternalToolRunner();
        var runner = new SystemNodeCommandRunner(tools);

        Assert.Throws<ArgumentNullException>(() => new SystemNodeCommandRunner(null!));
        Assert.Throws<ArgumentNullException>(() => runner.Run(null!));
        Assert.Throws<ArgumentException>(() => runner.Run(new(" ", ScriptPath, [])));
        Assert.Throws<ArgumentException>(() => runner.Run(new("node", ScriptPath, [])));
        Assert.Throws<ArgumentException>(() => runner.Run(new(NodePath, " ", [])));
        Assert.Throws<ArgumentException>(() => runner.Run(new(NodePath, "script.mjs", [])));
        Assert.Throws<ArgumentException>(() => runner.Run(new(NodePath, ScriptPath, default)));
        Assert.Null(tools.Invocation);
    }

    private sealed class RecordingConfiguredExternalToolRunner : IConfiguredExternalToolRunner
    {
        public ExternalToolInvocation? Invocation { get; private set; }

        public ToolResult Run(ExternalToolInvocation invocation)
        {
            Invocation = invocation;
            return new(5, "result", string.Empty);
        }
    }
}
