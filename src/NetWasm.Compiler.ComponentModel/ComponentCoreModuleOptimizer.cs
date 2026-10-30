using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleOptimizer
{
    void Optimize(
        string inputPath,
        string outputPath,
        ComponentTarget target,
        FinalWasmOptimization optimization);
}

public sealed class ComponentCoreModuleOptimizer(
    IBinaryenToolRunner tools,
    IFileExistence files,
    IWasmCoreModuleValidator validator,
    IWasmCoreModuleInlineAnnotationEditor inlineAnnotations) : IComponentCoreModuleOptimizer
{
    private static readonly FrozenDictionary<FinalWasmOptimization, string>
        OptimizationFlags = new Dictionary<FinalWasmOptimization, string>
        {
            [FinalWasmOptimization.O0] = "-O0",
            [FinalWasmOptimization.O1] = "-O1",
            [FinalWasmOptimization.O2] = "-O2",
            [FinalWasmOptimization.O3] = "-O3",
            [FinalWasmOptimization.Os] = "-Os",
            [FinalWasmOptimization.Oz] = "-Oz",
        }.ToFrozenDictionary();

    private readonly IBinaryenToolRunner _tools = tools ??
        throw new ArgumentNullException(nameof(tools));
    private readonly IFileExistence _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IWasmCoreModuleValidator _validator = validator ??
        throw new ArgumentNullException(nameof(validator));
    private readonly IWasmCoreModuleInlineAnnotationEditor _inlineAnnotations = inlineAnnotations ??
        throw new ArgumentNullException(nameof(inlineAnnotations));

    public void Optimize(
        string inputPath,
        string outputPath,
        ComponentTarget target,
        FinalWasmOptimization optimization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Width is not ("wasm32" or "wasm64"))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (!Enum.IsDefined(optimization))
        {
            throw new ArgumentOutOfRangeException(nameof(optimization));
        }
        RequireFile(inputPath);
        if (optimization == FinalWasmOptimization.None)
        {
            _validator.Validate(inputPath, target);
            _inlineAnnotations.Rewrite(inputPath, outputPath, WasmInlineAnnotationSelection.Remove);
            return;
        }
        var arguments = new List<string>
        {
            inputPath,
            OptimizationFlags[optimization],
            "--converge",
            "--remove-unused-module-elements",
            "--strip-debug",
            "--enable-multimemory",
            "--enable-exception-handling",
            // Preserve exnref cleanup blocks when optimizing async process completion.
            "--enable-reference-types",
            "--enable-bulk-memory",
            "--enable-nontrapping-float-to-int",
            "--enable-simd",
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
            _inlineAnnotations.Rewrite(outputPath, outputPath, WasmInlineAnnotationSelection.Remove);
            _validator.Validate(outputPath, target);
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
