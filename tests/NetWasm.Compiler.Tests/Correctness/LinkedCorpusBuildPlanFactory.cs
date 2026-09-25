using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal enum LinkedCorpusBuildStage { NativeRuntime, Merge, ValidateMerged, Optimize, ValidateFinal }

internal sealed record LinkedCorpusToolPaths(
    string EmsdkRoot, string Merge, string Optimize, string Validate, string Node);

internal sealed record LinkedCorpusBuildRequest(
    string RepositoryRoot,
    string RunDirectory,
    string ApplicationModulePath,
    string RuntimeLayoutPath,
    WasmTarget Target,
    CorpusWasmForm Form,
    LinkedCorpusToolPaths Tools,
    TimeSpan Timeout);

internal sealed record LinkedCorpusBuildStep(LinkedCorpusBuildStage Stage, QualifiedProcessRequest Process);

internal sealed record LinkedCorpusBuildPlan(
    string RuntimeModulePath,
    string MergedModulePath,
    string OutputModulePath,
    ImmutableArray<LinkedCorpusBuildStep> Steps);

internal interface ILinkedCorpusBuildPlanFactory
{
    LinkedCorpusBuildPlan Create(LinkedCorpusBuildRequest request);
}

internal sealed class LinkedCorpusBuildPlanFactory : ILinkedCorpusBuildPlanFactory
{
    public LinkedCorpusBuildPlan Create(LinkedCorpusBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Tools);
        foreach (var path in new[]
        {
            request.RepositoryRoot, request.RunDirectory, request.ApplicationModulePath,
            request.RuntimeLayoutPath, request.Tools.EmsdkRoot, request.Tools.Merge,
            request.Tools.Optimize, request.Tools.Validate, request.Tools.Node,
        })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("linked corpus paths must be absolute", nameof(request));
        }
        if (request.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "process timeout must be positive");
        var target = request.Target switch
        {
            WasmTarget.Wasm32 => "wasm32",
            WasmTarget.Wasm64 => "wasm64",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "unsupported linked target"),
        };
        var configuration = request.Form switch
        {
            CorpusWasmForm.Direct => "debug",
            CorpusWasmForm.Optimized => "release",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "unsupported linked output form"),
        };
        var prefix = Path.Combine(request.RunDirectory, $"linked.{target}.{configuration}");
        var runtime = prefix + ".runtime.wasm";
        var merged = prefix + ".merged.wasm";
        var output = request.Form == CorpusWasmForm.Optimized ? prefix + ".final.wasm" : merged;
        var paths = new[] { request.ApplicationModulePath, request.RuntimeLayoutPath, runtime, merged, prefix + ".final.wasm" };
        if (paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new ArgumentException("linked input and output paths must be distinct", nameof(request));
        ImmutableArray<string> features =
        [
            "--enable-exception-handling", "--enable-bulk-memory",
            "--enable-multimemory", "--enable-nontrapping-float-to-int",
        ];
        if (request.Target == WasmTarget.Wasm64)
            features = features.Add("--enable-memory64");
        var steps = ImmutableArray.CreateBuilder<LinkedCorpusBuildStep>();
        Add(LinkedCorpusBuildStage.NativeRuntime, "bash",
            [Path.Combine(request.RepositoryRoot, "eng", "build-netwasm-runtime.sh"),
             "--runtime-layout", request.RuntimeLayoutPath, "--target", target,
             "--configuration", configuration, "--output", runtime],
            ImmutableDictionary<string, string>.Empty
                .Add("NETWASM_EMSDK_ROOT", request.Tools.EmsdkRoot)
                .Add("TMPDIR", request.RunDirectory));
        Add(LinkedCorpusBuildStage.Merge, request.Tools.Merge,
            [request.ApplicationModulePath, "netwasm.application.v1", runtime, "netwasm.runtime.v1",
             .. features, "-g", "-o", merged]);
        Add(LinkedCorpusBuildStage.ValidateMerged, request.Tools.Validate,
            ["validate", merged, "--features", "all"]);
        if (request.Form == CorpusWasmForm.Optimized)
        {
            Add(LinkedCorpusBuildStage.Optimize, request.Tools.Optimize,
                [merged, "-Oz", .. features, "--disable-compact-imports", "-o", output]);
            Add(LinkedCorpusBuildStage.ValidateFinal, request.Tools.Validate,
                ["validate", output, "--features", "all"]);
        }
        return new(runtime, merged, output, steps.ToImmutable());

        void Add(LinkedCorpusBuildStage stage, string tool, ImmutableArray<string> arguments,
            ImmutableDictionary<string, string>? environment = null) =>
            steps.Add(new(stage, new(tool, arguments, request.Timeout)
            {
                WorkingDirectory = request.RepositoryRoot,
                EnvironmentVariables = environment ?? ImmutableDictionary<string, string>.Empty,
            }));
    }
}
