using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedFailureArtifactWriter(
    CompilerCorrectnessEnvironment environment) : IGeneratedFailureArtifactWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public string Write(
        GeneratedCorpusCase generatedCase,
        Exception exception,
        SourceReductionResult? reduction = null)
    {
        ArgumentNullException.ThrowIfNull(generatedCase);
        ArgumentNullException.ThrowIfNull(exception);
        var directory = Path.Combine(
            Path.GetTempPath(),
            "netwasm-correctness",
            "generated-failures",
            generatedCase.Name);
        Directory.CreateDirectory(directory);
        var reducedPath = Path.Combine(directory, "reduced.cs");
        File.Delete(reducedPath);
        File.Delete(Path.Combine(directory, "minimized.cs"));
        File.WriteAllText(
            Path.Combine(directory, "original.cs"),
            generatedCase.Fixture.Source);
        if (reduction is { Changed: true })
        {
            File.WriteAllText(reducedPath, reduction.Reduced);
        }
        var record = new
        {
            generatedCase.Name,
            generatedCase.Family,
            generatedCase.Seed,
            generatedCase.Dimensions,
            Inputs = generatedCase.Fixture.Inputs,
            Reduction = reduction is null ? null : new
            {
                reduction.Attempts,
                reduction.Completed,
                reduction.Changed,
            },
            Error = exception.ToString(),
            environment.SdkVersion,
            Reproduce =
                $"NETWASM_GENERATED_CASE={generatedCase.Name} " +
                "dotnet test tests/NetWasm.Compiler.Tests/NetWasm.Compiler.Tests.csproj " +
                "--filter FullyQualifiedName~DeterministicGeneratedCorpusTests",
        };
        File.WriteAllText(
            Path.Combine(directory, "failure.json"),
            JsonSerializer.Serialize(record, SerializerOptions) + "\n");
        return directory;
    }
}
