using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilerResponseReaderTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void ReadsOnceOrPreservesOriginalFailureAndSecondaryEvidence(bool fails, int capture)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-response-reader-test-");
        try
        {
            var invocation = Invocation(Path.Combine(directory.FullName, "response.json"));
            File.WriteAllText(invocation.ResponsePath, "exact response bytes\r\n");
            var parser = new RecordingParser { Failure = fails ? new JsonException("original response error") : null };
            var writer = new RecordingWriter { Failure = capture is 1 or 3 ? new IOException("capture error") : null, Path = capture == 2 ? "" : "/owned/evidence" };
            if (capture == 3) writer.Failure!.Data["IncompleteEvidenceDirectory"] = "/owned/incomplete";
            var reader = Reader(parser, writer);
            if (!fails)
            {
                Assert.Same(parser.Response, reader.Read(invocation, Success()));
                Assert.Empty(writer.Calls);
            }
            else
            {
                var failure = Assert.Throws<InvalidOperationException>(() => reader.Read(invocation, Success()));
                Assert.Same(parser.Failure, failure.InnerException);
                Assert.Equal((invocation, Success(), parser.Failure), Assert.Single(writer.Calls));
                if (capture == 0)
                {
                    Assert.Contains("reproduction: /owned/evidence", failure.Message, StringComparison.Ordinal);
                    Assert.Empty(failure.Data);
                }
                else
                {
                    Assert.Contains("original failure retained", failure.Message, StringComparison.Ordinal);
                    if (capture is 1 or 3) Assert.Same(writer.Failure, failure.Data["CompilerEvidenceCaptureFailure"]);
                    else Assert.IsType<ArgumentException>(failure.Data["CompilerEvidenceCaptureFailure"]);
                    if (capture == 3) Assert.Contains("/owned/incomplete", failure.Message, StringComparison.Ordinal);
                }
            }
            Assert.Equal("exact response bytes\r\n", Assert.Single(parser.Calls));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void MissingResponseIsCapturedWithoutCallingParser()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-response-reader-test-");
        try
        {
            var parser = new RecordingParser();
            var writer = new RecordingWriter();
            var invocation = Invocation(Path.Combine(directory.FullName, "absent.json"));
            var failure = Assert.Throws<InvalidOperationException>(() => Reader(parser, writer).Read(invocation, Success()));

            var cause = Assert.IsType<FileNotFoundException>(failure.InnerException);
            Assert.Equal(invocation.ResponsePath, cause.FileName);
            Assert.Same(cause, Assert.Single(writer.Calls).Cause);
            Assert.Empty(parser.Calls);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidBoundaryDoesNotReadOrCapture(int invalid)
    {
        var parser = new RecordingParser();
        var writer = new RecordingWriter();
        Assert.ThrowsAny<ArgumentException>(() => Reader(parser, writer).Read(
            invalid == 0 ? null! : Invocation("unused"), invalid == 1 ? null! : Success() with { ExitCode = 1 }));
        Assert.Empty(parser.Calls);
        Assert.Empty(writer.Calls);
    }

    private static ICorpusCompilerResponseReader Reader(RecordingParser parser, RecordingWriter writer) =>
        Assert.IsAssignableFrom<ICorpusCompilerResponseReader>(new CorpusCompilerResponseReader(parser, writer));

    private static QualifiedProcessResult Success() => new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);

    private static CorpusCompilerInvocation Invocation(string response)
    {
        var artifact = new CorpusArtifact("input", "", "hash", "", "", []);
        return new(new(new("Response", "Response", "", [0]), CilProfile.Emitted, artifact, artifact, "unused"),
            WasmTarget.Wasm32, new("compiler", [], TimeSpan.FromSeconds(1)), "request", response, "module", null, [], []);
    }

    private sealed class RecordingParser : ICorpusCompilerResponseParser
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public CorpusCompilerResponse Response { get; } = new(ImmutableDictionary<int, string>.Empty, "hash", 0);
        public CorpusCompilerResponse Parse(string json)
        {
            Calls.Add(json);
            if (Failure is not null) throw Failure;
            return Response;
        }
    }

    private sealed class RecordingWriter : ICorpusCompilerFailureWriter
    {
        public List<(CorpusCompilerInvocation Invocation, QualifiedProcessResult Result, Exception? Cause)> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public string Path { get; init; } = "/owned/evidence";
        public string Write(CorpusCompilerInvocation invocation, QualifiedProcessResult result, Exception? responseFailure = null)
        {
            Calls.Add((invocation, result, responseFailure));
            if (Failure is not null) throw Failure;
            return Path;
        }
    }
}
