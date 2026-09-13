using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel;

public sealed class ProcessBinaryenToolRunner(
    IExternalToolRunner tools) : IBinaryenToolRunner
{
    private readonly IExternalToolRunner _tools = tools ??
        throw new ArgumentNullException(nameof(tools));

    public ToolResult Run(string toolId, ImmutableArray<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (!BinaryenToolIds.Required.Contains(toolId, StringComparer.Ordinal))
        {
            throw new UnsupportedBinaryenToolException(toolId);
        }
        if (arguments.IsDefault)
        {
            throw new ArgumentException(
                "Binaryen command arguments must be explicit.",
                nameof(arguments));
        }
        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
        }
        return _tools.Run(toolId, arguments);
    }
}
