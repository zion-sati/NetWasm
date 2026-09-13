using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentPackageOperationRunner
{
    void Run(IEnumerable<string> arguments, string operation);
}

public sealed class ComponentPackageOperationRunner(
    IWasmTools tools) : IComponentPackageOperationRunner
{
    private readonly IWasmTools _tools = tools ??
        throw new ArgumentNullException(nameof(tools));

    public void Run(IEnumerable<string> arguments, string operation)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var result = _tools.Run(arguments);
        if (result.ExitCode == 0)
        {
            return;
        }
        var error = result.StandardError.Replace('\r', ' ').Replace('\n', ' ').Trim();
        throw ComponentException.Tool(
            $"wasm-tools failed to {operation}: " +
            (error.Length == 0 ? "unknown error" : error));
    }
}
