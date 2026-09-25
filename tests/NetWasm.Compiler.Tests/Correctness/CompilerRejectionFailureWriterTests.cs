using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CompilerRejectionFailureWriterTests
{
    [Fact]
    public void UnexpectedDiagnosticIsArchivedBeforeCallerDisposesItsAssets()
    {
        var source = Directory.CreateTempSubdirectory("netwasm-rejection-retention-composition-");
        string? archive = null;
        try
        {
            var input = Path.Combine(source.FullName, "input.dll");
            var reference = Path.Combine(source.FullName, "reference.dll");
            File.WriteAllBytes(input, [1, 2]);
            File.WriteAllBytes(reference, [3, 4]);
            var processes = new UnexpectedDiagnosticProcess();
            var runner = Assert.IsAssignableFrom<ICompilerRejectionRunner>(new CompilerRejectionRunner(
                new MalformedCompilationRunner(CompilerCorrectnessEnvironment.Discover(), processes),
                new CompilerRejectionVerifier(), new CompilerRejectionFailureWriter()));

            var error = Assert.Throws<InvalidOperationException>(() => runner.Run(Case(input, reference, source.FullName)));

            Assert.Equal(1, processes.Calls);
            Assert.Equal("Compiler rejection did not match the exact expected diagnostic.", error.InnerException!.Message);
            const string marker = "; reproduction: ";
            Assert.Contains(marker, error.Message, StringComparison.Ordinal);
            archive = error.Message[(error.Message.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
            source.Delete(recursive: true);
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(archive, "input.dll")));
            Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(Path.Combine(archive, "reference.dll")));
            Assert.True(File.Exists(Path.Combine(archive, "request.json")));
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(archive, "rejection-failure.json")));
            Assert.Equal("actual", json.RootElement.GetProperty("Diagnostic").GetProperty("Message").GetString());
            Assert.True(json.RootElement.GetProperty("ProcessAvailable").GetBoolean());
            Assert.False(json.RootElement.GetProperty("PartialOutputIsUsable").GetBoolean());
            Assert.Empty(error.Data);
        }
        finally
        {
            if (Directory.Exists(source.FullName)) source.Delete(recursive: true);
            if (archive is not null) Directory.Delete(archive, recursive: true);
        }
    }

    [Fact]
    public void IncompleteCaptureRemainsOwnedWithoutReplacingTheRejectionFailure()
    {
        var source = Directory.CreateTempSubdirectory("netwasm-incomplete-rejection-test-");
        string? archive = null;
        try
        {
            var input = Path.Combine(source.FullName, "input.dll");
            var reference = Path.Combine(source.FullName, "reference.dll");
            File.WriteAllBytes(input, [1, 2]);
            File.WriteAllBytes(reference, [3, 4]);
            var processes = new UnexpectedDiagnosticProcess("\uD800");
            var runner = Assert.IsAssignableFrom<ICompilerRejectionRunner>(new CompilerRejectionRunner(
                new MalformedCompilationRunner(CompilerCorrectnessEnvironment.Discover(), processes),
                new CompilerRejectionVerifier(), new CompilerRejectionFailureWriter()));

            var failure = Assert.Throws<InvalidOperationException>(() => runner.Run(Case(input, reference, source.FullName)));

            Assert.Equal(1, processes.Calls);
            Assert.Equal("Compiler rejection did not match the exact expected diagnostic.", failure.InnerException!.Message);
            var capture = Assert.IsType<System.Text.EncoderFallbackException>(failure.Data["RejectionEvidenceCaptureFailure"]);
            archive = Assert.IsType<string>(capture.Data["IncompleteEvidenceDirectory"]);
            Assert.Contains(archive, failure.Message, StringComparison.Ordinal);
            source.Delete(recursive: true);
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(archive, "input.dll")));
            Assert.True(File.Exists(Path.Combine(archive, "request.json")));
            Assert.False(File.Exists(Path.Combine(archive, "rejection-failure.json")));
        }
        finally
        {
            if (Directory.Exists(source.FullName)) source.Delete(recursive: true);
            if (archive is not null) Directory.Delete(archive, recursive: true);
        }
    }

    private sealed class UnexpectedDiagnosticProcess(string standardOutput = "") : IQualifiedProcessRunner
    {
        public int Calls { get; private set; }
        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            File.WriteAllText(request.Arguments[2], """{"Code":1002,"Message":"actual","Method":"Entry::Run","IlOffset":2}""");
            return new(QualifiedProcessCompletion.Exited, 0, standardOutput, "", TimeSpan.Zero);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ArchiveSurvivesCallerCleanupWithExactBytesAndExplicitMissingEvidence(bool observed, bool optional, bool process)
    {
        var source = Directory.CreateTempSubdirectory("netwasm-rejection-archive-test-");
        string? first = null;
        string? second = null;
        try
        {
            var bytesByPath = new Dictionary<string, byte[]>();
            var testCase = Case(Write("input.dll", [1, 2]), Write("reference.dll", [3, 4]), source.FullName);
            var prefix = Path.Combine(source.FullName, "attempt");
            if (observed) Write("attempt.request.json", [5]);
            if (optional)
            {
                Write("attempt.response.json", [6]);
                Write("attempt.wasm", [7]);
                Write("attempt.trace", [8]);
                Directory.CreateDirectory(prefix + ".trace.passes");
                Write("attempt.trace.passes/failure.json", [9]);
            }
            var responseFailure = optional ? new JsonException("response cause") : null;
            var observation = observed ? new MalformedCompilationObservation(QualifiedProcessCompletion.Exited,
                new(DiagnosticCode.InvalidCil, "actual", "Entry::Run", 2), optional, optional, "error")
            {
                ArtifactPrefix = prefix,
                ExitCode = process ? 134 : 0,
                ResponseFailure = responseFailure,
                Process = process ? new(QualifiedProcessCompletion.Exited, 134, "stdout\r\n", "stderr\n", TimeSpan.FromSeconds(1))
                {
                    LaunchException = optional ? new IOException("launch cause") : null,
                    CleanupFailure = "cleanup failure",
                    Termination = QualifiedProcessTermination.Forced,
                } : null,
            } : null;
            var cause = new InvalidOperationException("original rejection failure");

            first = Writer().Write(testCase, observation, cause);
            second = Writer().Write(testCase, observation, cause);
            source.Delete(recursive: true);

            Assert.NotEqual(first, second);
            Assert.False(File.Exists(Path.Combine(first, "rejection-failure.json.pending")));
            Assert.False(File.Exists(Path.Combine(second, "rejection-failure.json.pending")));
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "rejection-failure.json")));
            var root = json.RootElement;
            Assert.Equal("rejection-contract", root.GetProperty("Stage").GetString());
            Assert.Equal(observed, root.GetProperty("ObservationAvailable").GetBoolean());
            Assert.Equal(process, root.GetProperty("ProcessAvailable").GetBoolean());
            Assert.Equal(cause.ToString(), root.GetProperty("Failure").GetString());
            Assert.Equal(responseFailure?.ToString(), root.GetProperty("ResponseFailure").GetString());
            Assert.False(root.GetProperty("PartialOutputIsUsable").GetBoolean());
            Assert.Equal("expected", root.GetProperty("Case").GetProperty("Expectation").GetProperty("Diagnostic").GetProperty("Message").GetString());
            var files = root.GetProperty("Files").EnumerateArray().ToArray();
            Assert.Equal(observed ? optional ? 7 : 3 : 2, files.Length);
            foreach (var file in files)
            {
                var expected = bytesByPath[file.GetProperty("OriginalPath").GetString()!];
                Assert.Equal(expected, File.ReadAllBytes(Path.Combine(first, file.GetProperty("ArchivePath").GetString()!)));
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected)), file.GetProperty("Sha256").GetString());
            }
            Assert.Equal(process, File.Exists(Path.Combine(first, "compiler.stdout")));
            if (process)
            {
                Assert.Equal(134, root.GetProperty("ExitCode").GetInt32());
                Assert.Equal("Forced", root.GetProperty("Termination").GetString());
                Assert.Equal("cleanup failure", root.GetProperty("CleanupFailure").GetString());
                Assert.Equal(observation!.Process!.LaunchException?.ToString(), root.GetProperty("LaunchFailure").GetString());
                Assert.Equal("stdout\r\n", File.ReadAllText(Path.Combine(first, "compiler.stdout")));
                Assert.Equal("stderr\n", File.ReadAllText(Path.Combine(first, "compiler.stderr")));
            }

            string Write(string name, byte[] bytes)
            {
                var path = Path.Combine(source.FullName, name);
                File.WriteAllBytes(path, bytes);
                bytesByPath.Add(path, bytes);
                return path;
            }
        }
        finally
        {
            if (Directory.Exists(source.FullName)) source.Delete(recursive: true);
            if (first is not null) Directory.Delete(first, recursive: true);
            if (second is not null) Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void MissingRequiredInputIsExplicit()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.dll");
        var error = Assert.Throws<FileNotFoundException>(() => Writer().Write(Case(path, "unused", "unused"), null, new IOException()));
        Assert.Equal(path, error.FileName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidBoundaryFailsBeforeFileAccess(int defect)
    {
        var testCase = Case("unused", "unused", "unused");
        testCase = defect switch
        {
            0 => null!,
            1 => testCase with { Input = null! },
            2 => testCase with { Expectation = null! },
            _ => testCase,
        };
        Assert.Throws<ArgumentNullException>(() => Writer().Write(testCase, null, defect == 3 ? null! : new IOException()));
    }

    private static CompilerRejectionCase Case(string input, string reference, string directory) =>
        new(new("case", "invariant", input, "declared-sha", 0, "", ""), reference, "Entry", directory, 0,
            new(new(DiagnosticCode.UnsupportedCil, "expected", "Entry::Run", 0)));
    private static ICompilerRejectionFailureWriter Writer() =>
        Assert.IsAssignableFrom<ICompilerRejectionFailureWriter>(new CompilerRejectionFailureWriter());
}
