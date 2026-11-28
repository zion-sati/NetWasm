// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleOptimizer
{
    // Converging repeats the preset until the module stops shrinking. With low memory unused,
    // wasm-opt folds constant address adds into load and store offsets, which is valid only
    // while nothing lives in the first KiB of memory. Keeping names keeps the module's name
    // section, which a debug build's debugger and trap backtraces read.
    void Optimize(
        string inputPath,
        string outputPath,
        ComponentTarget target,
        FinalWasmOptimization optimization,
        bool converge = false,
        bool lowMemoryUnused = false,
        bool keepNames = false);
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
            [FinalWasmOptimization.O1] = "-O1",
            [FinalWasmOptimization.O2] = "-O2",
            [FinalWasmOptimization.O3] = "-O3",
            [FinalWasmOptimization.Os] = "-Os",
            [FinalWasmOptimization.Oz] = "-Oz",
        }.ToFrozenDictionary();

    // In Binaryen's size measure, the expressions of a function's body. At 1,000 a routine of a few hundred
    // instructions, such as an image encoder's row filter, still joins its one caller, where it ran faster; the
    // decoder and encoder loops this limit is for are larger, and a limit of 2,000 gave back half of their gain.
    internal const string OneCallerInlineMaxFunctionSize = "1000";

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
        FinalWasmOptimization optimization,
        bool converge = false,
        bool lowMemoryUnused = false,
        bool keepNames = false)
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
        // O0 builds for speed and does not care about size, so like None it runs no
        // Binaryen passes: removing unused elements rewrote the whole module to drop a few.
        if (optimization is FinalWasmOptimization.None or FinalWasmOptimization.O0)
        {
            _validator.Validate(inputPath, target);
            _inlineAnnotations.Rewrite(inputPath, outputPath, WasmInlineAnnotationSelection.Remove);
            return;
        }
        var arguments = new List<string>
        {
            inputPath,
            OptimizationFlags[optimization],
            "--remove-unused-module-elements",
            keepNames ? "--debuginfo" : "--strip-debug",
            "--enable-multimemory",
            "--enable-exception-handling",
            // Preserve exnref cleanup blocks when optimizing async process completion.
            "--enable-reference-types",
            // Static native code may use multi-value blocks, as exception handling's exnref encoding does.
            "--enable-multivalue",
            "--enable-bulk-memory",
            "--enable-nontrapping-float-to-int",
            "--enable-simd",
        };
        if (target.Width == "wasm64")
        {
            arguments.Add("--enable-memory64");
        }
        if (optimization is FinalWasmOptimization.O3 or FinalWasmOptimization.Oz)
        {
            // Binaryen inlines every function that has one caller, at any size. At O3 that merged a decoder's pixel
            // loop and the routines around it into one function thousands of instructions long, whose loop the
            // engine's register allocator then ran with the memory base and the loop's pointers spilled to the
            // stack. A larger function with one caller stays a call, as .NET's JIT and LLVM keep one past their
            // inlining budget. Oz takes the limit too: the same merged functions ran its loops slower, and the limit
            // costs a module next to nothing in size. The other presets keep Binaryen's rule.
            arguments.Add("--one-caller-inline-max-function-size");
            arguments.Add(OneCallerInlineMaxFunctionSize);
        }
        if (converge)
        {
            arguments.Insert(2, "--converge");
        }
        if (lowMemoryUnused)
        {
            arguments.Add("--low-memory-unused");
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
        throw ComponentException.Tool(
            "wasm-opt failed to optimize the component core module: " + result.FailureDescription);
    }

    private void RequireFile(string path)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"core module '{path}' does not exist");
        }
    }
}
