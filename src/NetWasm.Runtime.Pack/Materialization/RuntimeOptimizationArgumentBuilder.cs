using System;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeOptimizationArgumentBuilder :
    IRuntimeOptimizationArgumentBuilder
{
    public ImmutableArray<string> Build(RuntimeOptimizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outputPath = Path.GetFullPath(request.OutputPath);
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.Add("--strip-target-features");
        arguments.Add("--post-emscripten");
        arguments.Add("-Oz");
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
