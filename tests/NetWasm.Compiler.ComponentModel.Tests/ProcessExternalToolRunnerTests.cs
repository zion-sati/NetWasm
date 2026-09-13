using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class ProcessExternalToolRunnerTests
{
    [Fact]
    public void SimpleRunCreatesAnExplicitInvocation()
    {
        var execution = new RecordingProcessExecution();
        var runner = Assert.IsAssignableFrom<IExternalToolRunner>(
            new ProcessExternalToolRunner(execution));

        var result = runner.Run("tool", ["--version"]);

        Assert.Equal(new ToolResult(3, "version", string.Empty), result);
        Assert.Equal("tool", execution.Invocation!.Executable);
        Assert.Equal(["--version"], execution.Invocation.Arguments);
        Assert.Empty(execution.Invocation.EnvironmentVariablePrefixesToRemove);
    }

    [Fact]
    public void ConfiguredRunForwardsAnImmutableInvocation()
    {
        var execution = new RecordingProcessExecution();
        var runner = Assert.IsAssignableFrom<IConfiguredExternalToolRunner>(
            new ProcessExternalToolRunner(execution));
        var invocation = new ExternalToolInvocation(
            "/tools/node",
            ["script.mjs", "--inspect"],
            ["NODE_", "NPM_CONFIG_"]);

        var result = runner.Run(invocation);

        Assert.Equal(new ToolResult(3, "version", string.Empty), result);
        Assert.Same(invocation, execution.Invocation);
    }

    [Fact]
    public void ConstructorAndSimpleRunRejectMissingInputs()
    {
        var execution = new RecordingProcessExecution();
        var runner = new ProcessExternalToolRunner(execution);

        Assert.Throws<ArgumentNullException>(() => new ProcessExternalToolRunner(null!));
        var executableException = Assert.Throws<ArgumentException>(() =>
            runner.Run(" ", null!));
        Assert.Equal("executable", executableException.ParamName);
        Assert.Throws<ArgumentNullException>(() => runner.Run("tool", null!));
        Assert.Null(execution.Invocation);
    }

    [Fact]
    public void ConfiguredRunRejectsMissingOrImplicitInputsBeforeExecution()
    {
        var execution = new RecordingProcessExecution();
        var runner = Assert.IsAssignableFrom<IConfiguredExternalToolRunner>(
            new ProcessExternalToolRunner(execution));
        ImmutableArray<string> nullArguments = [null!];

        Assert.Throws<ArgumentNullException>(() => runner.Run(null!));
        Assert.Throws<ArgumentException>(() => runner.Run(new(" ", [], [])));
        Assert.Throws<ArgumentException>(() => runner.Run(new("tool", default, [])));
        Assert.Throws<ArgumentException>(() => runner.Run(new("tool", [], default)));
        Assert.Throws<ArgumentException>(() => runner.Run(new(
            "tool",
            nullArguments,
            [])));
        Assert.Null(execution.Invocation);
    }

    [Fact]
    public void ConfiguredRunRejectsInvalidEnvironmentRemovalPrefixesBeforeExecution()
    {
        var execution = new RecordingProcessExecution();
        var runner = Assert.IsAssignableFrom<IConfiguredExternalToolRunner>(
            new ProcessExternalToolRunner(execution));

        Assert.Throws<ArgumentException>(() => runner.Run(new("tool", [], [" "])));
        Assert.Throws<ArgumentException>(() => runner.Run(new(
            "tool",
            [],
            ["NODE_", "node_"])));
        Assert.Null(execution.Invocation);
    }

    private sealed class RecordingProcessExecution : IProcessExecution
    {
        public ExternalToolInvocation? Invocation { get; private set; }

        public ToolResult Execute(ExternalToolInvocation invocation)
        {
            Invocation = invocation;
            return new(3, "version", string.Empty);
        }
    }
}
