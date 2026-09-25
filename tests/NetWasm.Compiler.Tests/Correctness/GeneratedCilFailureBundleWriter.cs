using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilFailureBundleWriter(
    IGeneratedCilSerializer serializer,
    IGeneratedCilManifestWriter manifests,
    CompilerCorrectnessEnvironment environment) :
    IGeneratedCilFailureBundleWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public string Write(GeneratedCilFailureBundleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (GeneratedCilReductionScore.Measure(request.Reduction.Reduced)
                .CompareTo(GeneratedCilReductionScore.Measure(
                    request.Reduction.Original)) >= 0)
        {
            throw new InvalidDataException(
                "a generated failure bundle cannot label an unchanged case as reduced");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReproduceCommand);
        Directory.CreateDirectory(request.OutputDirectory);
        var originalMethod = serializer.SerializeMethod(
            request.Reduction.Original.Program);
        var reducedMethod = serializer.SerializeMethod(
            request.Reduction.Reduced.Program);
        File.WriteAllBytes(
            Path.Combine(request.OutputDirectory, "original.cil"),
            originalMethod.Cil);
        File.WriteAllBytes(
            Path.Combine(request.OutputDirectory, "reduced.cil"),
            reducedMethod.Cil);
        manifests.Write(
            Path.Combine(request.OutputDirectory, "original-generated-cil.json"),
            request.Reduction.Original.Program,
            artifact: request.CompilerArtifact);
        manifests.Write(
            Path.Combine(request.OutputDirectory, "reduced-generated-cil.json"),
            request.Reduction.Reduced.Program,
            artifact: request.CompilerArtifact);
        CopyRequired(request.OriginalAssemblyPath, "original.dll");
        CopyRequired(request.ReducedAssemblyPath, "reduced.dll");
        CopyRequired(request.CoreClrTracePath, "coreclr.trace.json");
        CopyRequired(request.NetWasmTracePath, "netwasm.trace.json");
        CopyRequired(request.CompilerTracePath, "compiler.trace.txt");
        CopyRequired(request.ComplexityMetricsPath, "complexity-metrics.json");
        CopyRequired(request.CfgPath, "cfg.txt");
        CopyRequired(request.WasmPath, "application.wasm");
        var record = new
        {
            request.Reduction.Fingerprint,
            request.Reduction.TimedOut,
            request.Reduction.Attempts,
            request.Reduction.AppliedPasses,
            OriginalInputs = request.Reduction.Original.Inputs,
            ReducedInputs = request.Reduction.Reduced.Inputs,
            OriginalSeed = request.Reduction.Original.Program.Seed,
            ReducedSeed = request.Reduction.Reduced.Program.Seed,
            request.CompilerArtifact.CompilerVersion,
            request.CompilerArtifact.CompilerOptions,
            environment.SdkVersion,
            request.ReproduceCommand,
        };
        File.WriteAllText(
            Path.Combine(request.OutputDirectory, "reduction.json"),
            JsonSerializer.Serialize(record, SerializerOptions) + "\n");
        return request.OutputDirectory;

        void CopyRequired(string source, string name)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"generated failure evidence '{name}' is missing",
                    source);
            }
            File.Copy(
                source,
                Path.Combine(request.OutputDirectory, name),
                overwrite: true);
        }
    }
}
