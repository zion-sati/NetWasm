using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCompilerFailureWriter
{
    string Write(CorpusCompilerInvocation invocation, QualifiedProcessResult result, Exception? responseFailure = null);
}

internal sealed class CorpusCompilerFailureWriter(ICorpusReplayCommandFormatter replay) : ICorpusCompilerFailureWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Write(CorpusCompilerInvocation invocation, QualifiedProcessResult result, Exception? responseFailure = null)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(invocation.Compilation);
        if ((result.Succeeded && responseFailure is null) || invocation.ReferencePaths.IsDefault || invocation.SourcePaths.IsDefault)
        {
            throw new ArgumentException("Compiler evidence requires a process or response failure and explicit input lists.");
        }
        var compilation = invocation.Compilation;
        var fixture = compilation.Fixture;
        var reproduce = replay.Format(fixture.ReplayTestMethod, fixture.Matrix?.CellId,
            fixture.ReplayInput, fixture.Matrix?.Profile);
        var inputs = new List<(string OriginalPath, string ArchivePath)>
        {
            (compilation.Desktop.AssemblyPath, "inputs/desktop.dll"),
            (compilation.NetWasm.AssemblyPath, "inputs/netwasm.dll"),
            (invocation.RequestPath, "request.json"),
        };
        AddIndexed(invocation.ReferencePaths, "references", ".dll");
        AddIndexed(invocation.SourcePaths, "sources", ".cs");
        foreach (var (original, _) in inputs)
        {
            if (!File.Exists(original))
            {
                throw new FileNotFoundException("Required compiler failure input is missing.", original);
            }
        }
        AddOptional(compilation.Desktop.PdbPath, "inputs/desktop.pdb");
        AddOptional(compilation.NetWasm.PdbPath, "inputs/netwasm.pdb");
        AddOptional(invocation.ResponsePath, "response.json");
        AddOptional(invocation.ModulePath, "partial-output.wasm");
        AddOptional(invocation.TracePath, "compiler-trace.txt");

        var directory = Directory.CreateTempSubdirectory("netwasm-compiler-failure-").FullName;
        try
        {
            var files = new List<ArchivedInput>();
            foreach (var (original, relative) in inputs)
            {
                var destination = Path.Combine(directory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(original, destination);
                files.Add(new(original, relative, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(destination)))));
            }
            File.WriteAllText(Path.Combine(directory, "compiler.stdout"), result.StandardOutput);
            File.WriteAllText(Path.Combine(directory, "compiler.stderr"), result.StandardError);
            File.WriteAllText(Path.Combine(directory, "compiler-failure.json.pending"), JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Stage = responseFailure is null ? "compiler-process" : "compiler-response",
                ResponseFailure = responseFailure?.ToString(),
                fixture.Name,
                fixture.CaseId,
                fixture.FeatureIds,
                fixture.Inputs,
                CilProfile = compilation.Profile.ToString(),
                Target = invocation.Target.ToString(),
                MatrixProfile = fixture.Matrix?.Profile.ToString(),
                ReplayCell = fixture.Matrix?.CellId,
                fixture.ReplayInput,
                fixture.ReplayTestMethod,
                Reproduce = reproduce,
                invocation.Request.FileName,
                invocation.Request.Arguments,
                invocation.Request.WorkingDirectory,
                invocation.Request.Timeout,
                Completion = result.Completion.ToString(),
                result.ExitCode,
                result.Elapsed,
                Termination = result.Termination.ToString(),
                result.CleanupFailure,
                LaunchFailure = result.LaunchException?.ToString(),
                compilation.Desktop,
                compilation.NetWasm,
                PartialOutputIsUsable = false,
                Files = files,
            }, Options) + "\n");
            File.Move(Path.Combine(directory, "compiler-failure.json.pending"), Path.Combine(directory, "compiler-failure.json"));
            return directory;
        }
        catch (Exception failure)
        {
            failure.Data["IncompleteEvidenceDirectory"] = directory;
            throw;
        }

        void AddIndexed(ImmutableArray<string> paths, string category, string extension)
        {
            for (var index = 0; index < paths.Length; index++)
            {
                inputs.Add((paths[index], category + "/" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + extension));
            }
        }

        void AddOptional(string? original, string relative)
        {
            if (original is not null && File.Exists(original))
            {
                inputs.Add((original, relative));
            }
        }
    }

    private sealed record ArchivedInput(string OriginalPath, string ArchivePath, string Sha256);
}
