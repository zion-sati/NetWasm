using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmTools
{
    ToolResult Run(params IEnumerable<string> arguments);
}

public sealed class ProcessWasmTools : IWasmTools
{
    private readonly IExternalToolRunner _tools;
    private readonly ExternalToolCommand _command;

    public ProcessWasmTools(
        IExternalToolRunner tools,
        string executable = "wasm-tools")
        : this(tools, new ExternalToolCommand(executable, []))
    {
    }

    public ProcessWasmTools(
        IExternalToolRunner tools,
        ExternalToolCommand command)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Executable))
        {
            throw new ArgumentException(
                "wasm-tools command executable cannot be empty",
                nameof(command));
        }
        if (command.ArgumentPrefix.IsDefault
            || command.ArgumentPrefix.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "wasm-tools command arguments must be explicit and nonempty",
                nameof(command));
        }
        _command = command;
    }

    public ToolResult Run(params IEnumerable<string> arguments) =>
        _tools.Run(
            _command.Executable,
            [.. _command.ArgumentPrefix, .. arguments]);
}
