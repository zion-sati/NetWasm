using System;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleOptimizer
{
    void Optimize(string inputPath, string outputPath, ComponentTarget target);
}

public sealed class ComponentCoreModuleOptimizer(
    IBinaryenToolRunner tools,
    IFileExistence files) : IComponentCoreModuleOptimizer
{
    private readonly IBinaryenToolRunner _tools = tools ??
        throw new ArgumentNullException(nameof(tools));
    private readonly IFileExistence _files = files ??
        throw new ArgumentNullException(nameof(files));

    public void Optimize(string inputPath, string outputPath, ComponentTarget target)
    {
        RequireFile(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(target);
        var arguments = new List<string>
        {
            inputPath,
            "-Oz",
            "--remove-unused-module-elements",
            "--strip-debug",
            "--enable-multimemory",
            "--enable-exception-handling",
            "--enable-bulk-memory",
            "--enable-nontrapping-float-to-int",
        };
        if (target.Width == "wasm64")
        {
            arguments.Add("--enable-memory64");
        }
        arguments.Add("--output");
        arguments.Add(outputPath);
        var result = _tools.Run(BinaryenToolIds.WasmOpt, [.. arguments]);
        if (result.ExitCode == 0)
        {
            return;
        }
        var error = result.StandardError.Replace('\r', ' ')
            .Replace('\n', ' ').Trim();
        throw ComponentException.Tool(
            "wasm-opt failed to optimize the component core module: " +
            (error.Length == 0 ? "unknown error" : error));
    }

    private void RequireFile(string path)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"core module '{path}' does not exist");
        }
    }
}
