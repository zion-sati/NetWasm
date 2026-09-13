using System;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ComponentCoreModuleMergeRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string EnvironmentModulePath,
    string OutputPath,
    ComponentTarget Target,
    string? HostModulePath = null,
    string? ManagedExecutableAdapterModulePath = null);

public interface IComponentCoreModuleMergeRunner
{
    void Run(ComponentCoreModuleMergeRequest request);
}

public sealed class ComponentCoreModuleMergeRunner(
    IBinaryenToolRunner tools) : IComponentCoreModuleMergeRunner
{
    private readonly IBinaryenToolRunner _tools = tools ??
        throw new ArgumentNullException(nameof(tools));

    public void Run(ComponentCoreModuleMergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = new List<string>
        {
            request.RuntimeModulePath,
            "netwasm.runtime.v1",
            request.ApplicationModulePath,
            "netwasm.application.v1",
            request.EnvironmentModulePath,
            "env",
        };
        if (request.HostModulePath is not null)
        {
            arguments.Add(request.HostModulePath);
            arguments.Add(RuntimeAbi.HostModule);
        }
        if (request.ManagedExecutableAdapterModulePath is not null)
        {
            arguments.Add(request.ManagedExecutableAdapterModulePath);
            arguments.Add("netwasm.command.v1");
        }
        arguments.Add("--output");
        arguments.Add(request.OutputPath);
        arguments.Add("--enable-multimemory");
        arguments.Add("--enable-exception-handling");
        arguments.Add("--enable-bulk-memory");
        arguments.Add("--enable-nontrapping-float-to-int");
        if (request.Target.Width == "wasm64")
        {
            arguments.Add("--enable-memory64");
        }
        var result = _tools.Run(BinaryenToolIds.WasmMerge, [.. arguments]);
        if (result.ExitCode == 0)
        {
            return;
        }
        var error = result.StandardError.Replace('\r', ' ')
            .Replace('\n', ' ').Trim();
        throw ComponentException.Tool(
            "wasm-merge failed to link the NetWasm runtime: " +
            (error.Length == 0 ? "unknown error" : error));
    }
}
