using System.Security.Cryptography;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICompilerRejectionFailureWriter
{
    string Write(CompilerRejectionCase testCase, MalformedCompilationObservation? observation, Exception failure);
}

internal sealed class CompilerRejectionFailureWriter : ICompilerRejectionFailureWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Write(CompilerRejectionCase testCase, MalformedCompilationObservation? observation, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(testCase.Input);
        ArgumentNullException.ThrowIfNull(testCase.Expectation);
        ArgumentNullException.ThrowIfNull(failure);
        var files = new List<(string Original, string Archived)>
        {
            (testCase.Input.Path, "input.dll"),
            (testCase.ReferencePath, "reference.dll"),
        };
        if (observation?.ArtifactPrefix is { } prefix)
        {
            files.Add((prefix + ".request.json", "request.json"));
            AddOptional(prefix + ".response.json", "response.json");
            AddOptional(prefix + ".wasm", "partial-output.wasm");
            AddOptional(prefix + ".trace", "compiler-trace.txt");
            AddOptional(prefix + ".trace.passes/failure.json", "failure-snapshot.json");
        }
        foreach (var (original, _) in files)
        {
            if (!File.Exists(original)) throw new FileNotFoundException("Required rejection evidence input is missing.", original);
        }
        var directory = Directory.CreateTempSubdirectory("netwasm-rejection-failure-").FullName;
        try
        {
            var archived = new List<ArchivedFile>();
            foreach (var (original, name) in files)
            {
                var destination = Path.Combine(directory, name);
                File.Copy(original, destination);
                archived.Add(new(original, name, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(destination)))));
            }
            var process = observation?.Process;
            if (process is not null)
            {
                File.WriteAllText(Path.Combine(directory, "compiler.stdout"), process.StandardOutput);
                File.WriteAllText(Path.Combine(directory, "compiler.stderr"), process.StandardError);
            }
            File.WriteAllText(Path.Combine(directory, "rejection-failure.json.pending"), JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Stage = "rejection-contract",
                Case = testCase,
                ObservationAvailable = observation is not null,
                Diagnostic = observation?.Diagnostic,
                Completion = observation?.Completion.ToString(),
                ExitCode = observation?.ExitCode,
                ModuleExists = observation?.ModuleExists,
                FailureSnapshotExists = observation?.FailureSnapshotExists,
                ArtifactPrefix = observation?.ArtifactPrefix,
                ResponseFailure = observation?.ResponseFailure?.ToString(),
                ProcessAvailable = process is not null,
                Elapsed = process?.Elapsed,
                Termination = process?.Termination.ToString(),
                CleanupFailure = process?.CleanupFailure,
                LaunchFailure = process?.LaunchException?.ToString(),
                Failure = failure.ToString(),
                PartialOutputIsUsable = false,
                Files = archived,
            }, Options) + "\n");
            File.Move(Path.Combine(directory, "rejection-failure.json.pending"), Path.Combine(directory, "rejection-failure.json"));
            return directory;
        }
        catch (Exception captureFailure)
        {
            captureFailure.Data["IncompleteEvidenceDirectory"] = directory;
            throw;
        }

        void AddOptional(string original, string name)
        {
            if (File.Exists(original)) files.Add((original, name));
        }
    }

    private sealed record ArchivedFile(string OriginalPath, string ArchivePath, string Sha256);
}
