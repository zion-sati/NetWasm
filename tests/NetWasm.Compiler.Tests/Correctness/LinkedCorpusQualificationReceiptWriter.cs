using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusQualificationReceiptWriter
{
    string Write(CorpusCompilation compilation, LinkedCorpusExecution execution,
        ImmutableDictionary<int, OracleObservation> expected);
}

// Persists execution evidence. Semantic acceptance is owned by the comparer.
internal sealed class LinkedCorpusQualificationReceiptWriter(
    ILinkedCorpusReceiptDestinationValidator destinations,
    ICorpusArtifactFingerprint fingerprints,
    ICorpusSourceNamesVerifier sourceNames) : ILinkedCorpusQualificationReceiptWriter
{
    private sealed record Artifact(string Name, string Path, string Sha256);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Write(CorpusCompilation compilation, LinkedCorpusExecution execution,
        ImmutableDictionary<int, OracleObservation> expected)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(expected);
        var resolvedRoot = destinations.Validate(compilation.Directory);
        if (execution.BuildSteps.IsDefaultOrEmpty || execution.BuildSteps.Any(step => !step.Result.Succeeded))
            throw new ArgumentException("Linked receipts require successful build steps.", nameof(execution));
        var inputs = compilation.Fixture.Inputs;
        if (inputs.IsDefaultOrEmpty || inputs.Distinct().Count() != inputs.Length ||
            expected.Count != inputs.Length || execution.Observations.Count != inputs.Length ||
            inputs.Any(input => !expected.ContainsKey(input) || !execution.Observations.ContainsKey(input)))
            throw new ArgumentException("Linked receipt observations must match the exact corpus inputs.");
        if (!compilation.Sources.IsEmpty)
            sourceNames.Verify([.. compilation.Sources.Select(source => source.Name)]);

        var artifacts = new List<Artifact>
        {
            new("desktop.dll", compilation.Desktop.AssemblyPath, compilation.Desktop.AssemblySha256),
            new("netwasm.dll", compilation.NetWasm.AssemblyPath, compilation.NetWasm.AssemblySha256),
            new("application.wasm", execution.ApplicationModulePath, execution.ApplicationModuleSha256),
            new("runtime-layout.json", execution.RuntimeLayoutPath, execution.RuntimeLayoutSha256),
            new("interop-manifest.json", execution.InteropManifestPath, execution.InteropManifestSha256),
            new("linked.wasm", execution.ModulePath, execution.ModuleSha256),
        };
        artifacts.AddRange(compilation.Sources.Select(source =>
            new Artifact("sources/" + source.Name, source.Path, source.Sha256)));
        Capture("compiler.request.json", execution.ApplicationModulePath + ".request.json");
        Capture("compiler.response.json", execution.ApplicationModulePath + ".response.json");
        Capture("observation.request.json", execution.ModulePath + ".observation.request.json");
        Capture("observation.response.json", execution.ModulePath + ".observation.response.json");
        var generated = Path.Combine(compilation.Directory, "generated-cil.json");
        if (File.Exists(generated)) Capture("generated-cil.json", generated);
        foreach (var artifact in artifacts)
            Verify(artifact.Path, artifact.Sha256);

        var bundle = Path.Combine(resolvedRoot, "linked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        try
        {
            foreach (var artifact in artifacts)
            {
                var path = Path.Combine(bundle, artifact.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(artifact.Path, path, overwrite: false);
                Verify(path, artifact.Sha256);
            }
            var receipt = new
            {
                SchemaVersion = 1,
                EvidenceKind = "linked-execution",
                compilation.Fixture.CaseId,
                compilation.Fixture.Name,
                compilation.Fixture.FeatureIds,
                Cell = new CorpusMatrixCell(compilation.Profile, execution.Target,
                    execution.Form, CorpusExecutionBackend.Linked).Id,
                compilation.Fixture.OracleMode,
                compilation.Fixture.ReplayTestMethod,
                compilation.NetWasm.CompilerVersion,
                compilation.NetWasm.CompilerOptions,
                Artifacts = artifacts.Select(artifact => new { artifact.Name, artifact.Sha256 }),
                Observations = inputs.Select(input => new
                {
                    Input = input,
                    Desktop = expected[input],
                    Linked = execution.Observations[input],
                }),
                BuildSteps = execution.BuildSteps.Select(step => new
                {
                    step.Step.Stage,
                    step.Step.Process.FileName,
                    step.Step.Process.Arguments,
                    step.Step.Process.WorkingDirectory,
                    step.Step.Process.EnvironmentVariables,
                    step.Result.Completion,
                    step.Result.ExitCode,
                    step.Result.Elapsed,
                }),
            };
            var pending = Path.Combine(bundle, "receipt.pending");
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, receipt, Options);
            File.Move(pending, Path.Combine(bundle, "receipt.json"));
            return bundle;
        }
        catch (Exception failure)
        {
            failure.Data["IncompleteEvidenceDirectory"] = bundle;
            throw;
        }

        void Capture(string name, string path) => artifacts.Add(new(name, path, fingerprints.Compute(path)));

        void Verify(string path, string hash)
        {
            if (!StringComparer.Ordinal.Equals(hash, fingerprints.Compute(path)))
                throw new InvalidOperationException("Linked receipt artifact identity changed.");
        }
    }
}
