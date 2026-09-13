using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Compiler.ComponentModel.Node;

public sealed record SystemNodeCommand(
    string NodePath,
    string ScriptPath,
    ImmutableArray<string> Arguments);

public interface ISystemNodeCommandRunner
{
    ToolResult Run(SystemNodeCommand command);
}

public sealed class SystemNodeCommandRunner(
    IConfiguredExternalToolRunner tools) : ISystemNodeCommandRunner
{
    private readonly IConfiguredExternalToolRunner _tools = tools ??
        throw new ArgumentNullException(nameof(tools));

    public ToolResult Run(SystemNodeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAbsolutePath(command.NodePath, nameof(command.NodePath));
        RequireAbsolutePath(command.ScriptPath, nameof(command.ScriptPath));
        if (command.Arguments.IsDefault)
        {
            throw new ArgumentException("Node command arguments must be explicit.", nameof(command));
        }

        return _tools.Run(new(
            command.NodePath,
            [command.ScriptPath, .. command.Arguments],
            ["NODE_", "NPM_CONFIG_"]));
    }

    private static void RequireAbsolutePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "System Node executable and package script paths must be absolute.",
                parameterName);
        }
    }
}
