using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Runtime.Pack.Planning;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeOptimizationArgumentBuilder :
    IRuntimeOptimizationArgumentBuilder
{
    private static readonly FrozenDictionary<RuntimeWasmOptimization, string>
        OptimizationFlags = new Dictionary<RuntimeWasmOptimization, string>
        {
            [RuntimeWasmOptimization.O0] = "-O0",
            [RuntimeWasmOptimization.O1] = "-O1",
            [RuntimeWasmOptimization.O2] = "-O2",
            [RuntimeWasmOptimization.O3] = "-O3",
            [RuntimeWasmOptimization.Os] = "-Os",
            [RuntimeWasmOptimization.Oz] = "-Oz",
        }.ToFrozenDictionary();

    public ImmutableArray<string> Build(RuntimeOptimizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Optimization))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (request.Optimization == RuntimeWasmOptimization.None)
        {
            return [];
        }
        var outputPath = Path.GetFullPath(request.OutputPath);
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add("--strip-target-features");
        arguments.Add("--post-emscripten");
        arguments.Add(OptimizationFlags[request.Optimization]);
        // Match Emscripten's optimized non-stack-first link. Binaryen may
        // treat the first KiB as unreachable only when static data starts at
        // or above the boundary enforced by that pass.
        if (request.RuntimeGlobalBase >= 1024)
        {
            arguments.Add("--low-memory-unused");
        }
        arguments.Add("--zero-filled-memory");
        arguments.Add("--pass-arg=directize-initial-contents-immutable");
        arguments.Add("--no-stack-ir");
        arguments.Add("--strip-debug");
        arguments.Add("--strip-producers");
        arguments.Add(outputPath);
        arguments.Add("-o");
        arguments.Add(outputPath);
        arguments.Add("--mvp-features");
        arguments.Add("--enable-bulk-memory");
        arguments.Add("--enable-bulk-memory-opt");
        arguments.Add("--enable-call-indirect-overlong");
        arguments.Add("--enable-multivalue");
        arguments.Add("--enable-mutable-globals");
        arguments.Add("--enable-nontrapping-float-to-int");
        arguments.Add("--enable-reference-types");
        arguments.Add("--enable-sign-ext");
        if (request.Target.Target == "wasm64")
        {
            arguments.Add("--enable-memory64");
        }

        return arguments.ToImmutable();
    }
}
