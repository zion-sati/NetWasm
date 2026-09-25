using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CompilerFailureArtifactWriter(
    CompilerCorrectnessEnvironment environment,
    ICorpusReplayCommandFormatter replay,
    ICorpusSourceNamesVerifier sourceNames) : ICompilerFailureArtifactWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public string Write(
        CorpusCompilation compilation,
        int input,
        OracleObservation desktop,
        OracleObservation netWasm,
        NetWasmExecution execution,
        string reason)
    {
        var reproduce = replay.Format(compilation.Fixture.ReplayTestMethod,
            compilation.Fixture.Matrix?.CellId, compilation.Fixture.ReplayInput, compilation.Fixture.Matrix?.Profile);
        if (!compilation.Sources.IsEmpty)
        {
            sourceNames.Verify([.. compilation.Sources.Select(source => source.Name)]);
        }
        var directory = Path.Combine(
            compilation.Directory,
            "failure-" + input.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var source in compilation.Sources)
        {
            var path = Path.Combine(directory, "sources", source.Name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(source.Path, path, overwrite: true);
        }
        if (compilation.Profile != CilProfile.Emitted)
        {
            File.Copy(
                Path.Combine(compilation.Directory, compilation.Fixture.Name + ".cs"),
                Path.Combine(directory, "fixture.cs"),
                overwrite: true);
        }
        File.Copy(compilation.Desktop.AssemblyPath,
            Path.Combine(directory, "desktop.dll"), overwrite: true);
        File.Copy(compilation.NetWasm.AssemblyPath,
            Path.Combine(directory, "netwasm.dll"), overwrite: true);
        File.Copy(execution.ModulePath,
            Path.Combine(directory, "application.wasm"), overwrite: true);
        if (execution.DiagnosticTracePath is { } diagnosticTracePath &&
            File.Exists(diagnosticTracePath))
        {
            File.Copy(diagnosticTracePath,
                Path.Combine(directory, "compiler-trace.txt"), overwrite: true);
        }
        var generatedCil = Path.Combine(compilation.Directory, "generated-cil.json");
        if (File.Exists(generatedCil))
        {
            File.Copy(generatedCil,
                Path.Combine(directory, "generated-cil.json"), overwrite: true);
        }
        var passDirectory = execution.DiagnosticTracePath is { } tracePath
            ? tracePath + ".passes"
            : null;
        if (passDirectory is not null && Directory.Exists(passDirectory))
        {
            CopyDirectory(passDirectory, Path.Combine(directory, "compiler-passes"));
            File.WriteAllText(
                Path.Combine(directory, "compiler-passes", "observations.json"),
                JsonSerializer.Serialize(new
                {
                    Desktop = desktop,
                    NetWasm = netWasm,
                }, SerializerOptions) + "\n");
        }
        var artifact = new
        {
            compilation.Fixture.Name,
            compilation.Fixture.CaseId,
            compilation.Fixture.FeatureIds,
            Profile = compilation.Profile.ToString(),
            Input = input,
            Reason = reason,
            Desktop = desktop,
            NetWasm = netWasm,
            compilation.Desktop.AssemblySha256,
            NetWasmAssemblySha256 = compilation.NetWasm.AssemblySha256,
            execution.ModuleSha256,
            execution.Target,
            execution.Backend,
            execution.Form,
            compilation.NetWasm.CompilerVersion,
            compilation.NetWasm.CompilerOptions,
            SourceFiles = compilation.Sources.Select(source => source with { Path = "sources/" + source.Name }),
            compilation.Fixture.ReplayTestMethod,
            compilation.Fixture.ReplayInput,
            ReplayCell = compilation.Fixture.Matrix?.CellId,
            MatrixProfile = compilation.Fixture.Matrix?.Profile.ToString(),
            ReplayScope = reproduce is null ? "unavailable" : "test-method-filter",
            Reproduce = reproduce,
            Repository = environment.RepositoryRoot,
        };
        File.WriteAllText(
            Path.Combine(directory, "failure.json"),
            JsonSerializer.Serialize(artifact, SerializerOptions) + "\n");
        return directory;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetFileName(file)),
                overwrite: true);
        }
        foreach (var child in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                child,
                Path.Combine(destination, Path.GetFileName(child)));
        }
    }
}
