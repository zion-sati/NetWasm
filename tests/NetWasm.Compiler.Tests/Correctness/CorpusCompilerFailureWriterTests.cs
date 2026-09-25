using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilerFailureWriterTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ArchivesExactInputBytesAndNeverTreatsPartialOutputAsUsable(bool optional, bool matrix, bool responseFailure)
    {
        var source = Directory.CreateTempSubdirectory("netwasm-failure-writer-test-");
        string? first = null;
        string? second = null;
        try
        {
            var invocation = CreateInvocation(source.FullName, optional);
            if (!matrix)
            {
                invocation = invocation with
                {
                    Compilation = invocation.Compilation with
                    {
                        Fixture = invocation.Compilation.Fixture with { Matrix = null },
                    },
                };
            }
            var replay = new RecordingReplay();
            var writer = Writer(replay);
            var cause = responseFailure ? new JsonException("original response error") : null;
            var result = new QualifiedProcessResult(QualifiedProcessCompletion.Exited, responseFailure ? 0 : 1, "stdout\r\n", "stderr\n", TimeSpan.FromSeconds(2))
            {
                Termination = QualifiedProcessTermination.Forced,
                CleanupFailure = "cleanup evidence",
                LaunchException = optional ? new IOException("original launch cause") : null,
            };

            first = writer.Write(invocation, result, cause);
            second = writer.Write(invocation, result, cause);

            Assert.NotEqual(first, second);
            Assert.False(File.Exists(Path.Combine(first, "compiler-failure.json.pending")));
            Assert.False(File.Exists(Path.Combine(second, "compiler-failure.json.pending")));
            Assert.Equal("stdout\r\n", File.ReadAllText(Path.Combine(first, "compiler.stdout")));
            Assert.Equal("stderr\n", File.ReadAllText(Path.Combine(first, "compiler.stderr")));
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "compiler-failure.json")));
            var root = document.RootElement;
            Assert.Equal(responseFailure ? "compiler-response" : "compiler-process", root.GetProperty("Stage").GetString());
            Assert.Equal(cause?.ToString(), root.GetProperty("ResponseFailure").GetString());
            Assert.Equal("failure.case", root.GetProperty("CaseId").GetString());
            Assert.Equal("S10", root.GetProperty("FeatureIds")[0].GetString());
            Assert.Equal(0, root.GetProperty("Inputs")[0].GetInt32());
            Assert.Equal("Emitted", root.GetProperty("CilProfile").GetString());
            Assert.Equal("Wasm64", root.GetProperty("Target").GetString());
            Assert.Equal(matrix ? "Family" : null, root.GetProperty("MatrixProfile").GetString());
            Assert.Equal(matrix ? "Emitted-Wasm64-Direct" : null, root.GetProperty("ReplayCell").GetString());
            Assert.Equal("exact replay", root.GetProperty("Reproduce").GetString());
            Assert.Equal("Exited", root.GetProperty("Completion").GetString());
            Assert.Equal(responseFailure ? 0 : 1, root.GetProperty("ExitCode").GetInt32());
            Assert.Equal("Forced", root.GetProperty("Termination").GetString());
            Assert.Equal("cleanup evidence", root.GetProperty("CleanupFailure").GetString());
            Assert.Equal(optional, root.GetProperty("LaunchFailure").ValueKind == JsonValueKind.String);
            Assert.False(root.GetProperty("PartialOutputIsUsable").GetBoolean());
            Assert.Equal("dotnet", root.GetProperty("FileName").GetString());
            Assert.Equal("compiler", root.GetProperty("Arguments")[0].GetString());
            Assert.Equal(invocation.Request.Timeout, root.GetProperty("Timeout").Deserialize<TimeSpan>());
            var files = root.GetProperty("Files").EnumerateArray().ToArray();
            Assert.Equal(optional ? 11 : 3, files.Length);
            foreach (var file in files)
            {
                var bytes = File.ReadAllBytes(file.GetProperty("OriginalPath").GetString()!);
                Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(first, file.GetProperty("ArchivePath").GetString()!)));
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), file.GetProperty("Sha256").GetString());
            }
            Assert.Equal(optional, File.Exists(Path.Combine(first, "partial-output.wasm")));
            Assert.Equal(optional, File.Exists(Path.Combine(first, "sources", "0001.cs")));
            Assert.All(replay.Calls, call => Assert.Equal(("Tests.Failure.Run", matrix ? "Emitted-Wasm64-Direct" : null,
                (int?)0, matrix ? (CorpusMatrixProfile?)CorpusMatrixProfile.Family : null), call));
            Assert.Equal(2, replay.Calls.Count);
        }
        finally
        {
            source.Delete(recursive: true);
            if (first is not null) Directory.Delete(first, recursive: true);
            if (second is not null) Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void MissingRequiredInputFailsBeforeAllocatingAnArchive()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-failure-writer-test-");
        try
        {
            var invocation = CreateInvocation(directory.FullName, false) with { ReferencePaths = [Path.Combine(directory.FullName, "missing.dll")] };
            var failure = Assert.Throws<FileNotFoundException>(() => Writer(new RecordingReplay()).Write(invocation, Failed()));
            Assert.Equal(invocation.ReferencePaths[0], failure.FileName);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InvalidEvidenceRequestFailsBeforeReplayOrFileAccess(int defect)
    {
        var artifact = new CorpusArtifact("unused", "", "sha", "", "", []);
        var invocation = new CorpusCompilerInvocation(new(new("Unused", "Unused", "", [0]), CilProfile.Emitted, artifact, artifact, "unused"),
            WasmTarget.Wasm32, new("unused", [], TimeSpan.Zero), "unused", "unused", "unused", null, [], []);
        invocation = defect switch
        {
            0 => null!,
            2 => invocation with { Compilation = null! },
            4 => invocation with { ReferencePaths = default },
            5 => invocation with { SourcePaths = default },
            _ => invocation,
        };
        var replay = new RecordingReplay();

        Assert.ThrowsAny<ArgumentException>(() => Writer(replay).Write(invocation, defect == 1 ? null! : defect == 3 ? Failed() with { ExitCode = 0 } : Failed()));
        Assert.Empty(replay.Calls);
    }

    [Fact]
    public void ReplayFailureIsNotReplacedWithAFileError()
    {
        var artifact = new CorpusArtifact("unused", "", "sha", "", "", []);
        var invocation = new CorpusCompilerInvocation(new(new("Unused", "Unused", "", [0]), CilProfile.Emitted, artifact, artifact, "unused"),
            WasmTarget.Wasm32, new("unused", [], TimeSpan.Zero), "unused", "unused", "unused", null, [], []);
        var cause = new InvalidOperationException("replay failed");
        var replay = new RecordingReplay { Failure = cause };

        Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => Writer(replay).Write(invocation, Failed())));
        Assert.Single(replay.Calls);
    }

    private static ICorpusCompilerFailureWriter Writer(RecordingReplay replay) =>
        Assert.IsAssignableFrom<ICorpusCompilerFailureWriter>(new CorpusCompilerFailureWriter(replay));

    [Fact]
    public void IncompleteCaptureRemainsOwnedWithoutReplacingTheCompilerFailure()
    {
        var source = Directory.CreateTempSubdirectory("netwasm-incomplete-compiler-test-");
        string? archive = null;
        try
        {
            var invocation = CreateInvocation(source.FullName, false);
            var verifier = Assert.IsAssignableFrom<ICorpusCompilerProcessVerifier>(
                new CorpusCompilerProcessVerifier(Writer(new RecordingReplay())));
            var result = new QualifiedProcessResult(QualifiedProcessCompletion.Exited, 134, "\uD800", "", TimeSpan.Zero);

            var failure = Assert.Throws<InvalidOperationException>(() => verifier.Verify(invocation, result));

            Assert.Contains("exit=134", failure.Message, StringComparison.Ordinal);
            var capture = Assert.IsType<System.Text.EncoderFallbackException>(failure.Data["CompilerEvidenceCaptureFailure"]);
            archive = Assert.IsType<string>(capture.Data["IncompleteEvidenceDirectory"]);
            Assert.Contains(archive, failure.Message, StringComparison.Ordinal);
            source.Delete(recursive: true);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(archive, "inputs", "desktop.dll")));
            Assert.True(File.Exists(Path.Combine(archive, "request.json")));
            Assert.False(File.Exists(Path.Combine(archive, "compiler-failure.json")));
        }
        finally
        {
            if (Directory.Exists(source.FullName)) source.Delete(recursive: true);
            if (archive is not null) Directory.Delete(archive, recursive: true);
        }
    }

    private static QualifiedProcessResult Failed() => new(QualifiedProcessCompletion.Exited, 1, "", "", TimeSpan.Zero);

    private static CorpusCompilerInvocation CreateInvocation(string directory, bool optional)
    {
        var input = Write("input.dll", [1, 2, 3]);
        var request = Write("request.json", [4, 5]);
        var pdb = optional ? Write("input.pdb", [6]) : string.Empty;
        var artifact = new CorpusArtifact(input, pdb, "declared-sha", "declared-pdb-sha", "producer", ["producer-option"]);
        var fixture = new CorpusFixture("Failure", "Failure", "", [0])
        {
            CaseId = "failure.case",
            FeatureIds = ["S10"],
            ReplayTestMethod = "Tests.Failure.Run",
            ReplayInput = 0,
            Matrix = new(CorpusMatrixProfile.Family, "Emitted-Wasm64-Direct"),
        };
        if (optional)
        {
            Write("response.json", [7]);
            Write("partial.wasm", [8]);
            Write("trace.txt", [9]);
        }
        return new(new(fixture, CilProfile.Emitted, artifact, artifact, directory), WasmTarget.Wasm64,
            new("dotnet", ["compiler", request, Path.Combine(directory, "response.json")], TimeSpan.FromSeconds(5)),
            request, Path.Combine(directory, "response.json"), Path.Combine(directory, "partial.wasm"),
            optional ? Path.Combine(directory, "trace.txt") : null,
            optional ? [Write("reference.dll", [10])] : [],
            optional ? [Write("first.cs", [11]), Write("second.cs", [12])] : []);

        string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }

    private sealed class RecordingReplay : ICorpusReplayCommandFormatter
    {
        public List<(string? Method, string? Cell, int? Input, CorpusMatrixProfile? Profile)> Calls { get; } = [];
        public Exception? Failure { get; init; }

        public string? Format(string? testMethod, string? cellId = null, int? input = null, CorpusMatrixProfile? profile = null)
        {
            Calls.Add((testMethod, cellId, input, profile));
            if (Failure is not null) throw Failure;
            return "exact replay";
        }
    }
}
