using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CompilerFailureArtifactWriterTests
{
    [Theory]
    [InlineData(false, 3, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    public void WriteCreatesSelfContainedReproductionWithProvenance(bool emitted, int traceState, bool generated)
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var directory = CorrectnessTestAssets.CreateDirectory();
        var hasReplayIdentity = emitted && traceState != 1;
        var fixture = CorrectnessTestAssets.CreateFixture("FailureArtifactUnit") with
        {
            CaseId = emitted ? "rtti.example" : null,
            FeatureIds = emitted ? ["S09", "S10"] : [],
            ReplayTestMethod = hasReplayIdentity ? "Example.EmittedTests.Run" : null,
            ReplayInput = emitted ? 3 : null,
            Matrix = emitted ? new(CorpusMatrixProfile.Extended, "Emitted-Wasm32-Direct") : null,
        };
        var replay = new RecordingReplayFormatter(hasReplayIdentity ? "exact replay command" : null);
        var source = Path.Combine(directory, fixture.Name + ".cs");
        var secondarySource = Path.Combine(directory, "support", "Part.cs");
        var desktop = Path.Combine(directory, "desktop-input.dll");
        var netWasm = Path.Combine(directory, "netwasm-input.dll");
        var module = Path.Combine(directory, "input.wasm");
        var trace = Path.Combine(directory, "trace.txt");
        if (!emitted)
        {
            File.WriteAllText(source, fixture.Source);
            Directory.CreateDirectory(Path.GetDirectoryName(secondarySource)!);
            File.WriteAllText(secondarySource, "secondary source\r\n");
        }
        File.WriteAllBytes(desktop, [1]);
        File.WriteAllBytes(netWasm, [2]);
        File.WriteAllBytes(module, [3]);
        if (traceState >= 2)
        {
            File.WriteAllText(trace, "test trace");
        }
        if (traceState == 3)
        {
            Directory.CreateDirectory(Path.Combine(trace + ".passes", "nested"));
            File.WriteAllText(Path.Combine(trace + ".passes", "01-metadata.json"), "{}");
            File.WriteAllText(Path.Combine(trace + ".passes", "nested", "detail.json"), "{}");
        }
        if (generated)
        {
            File.WriteAllText(Path.Combine(directory, "generated-cil.json"), "{}");
        }
        var artifact = new CorpusArtifact(
            desktop, desktop + ".pdb", "desktop-sha", "pdb-sha", "sdk", ["option"]);
        var compilation = new CorpusCompilation(
            fixture,
            emitted ? CilProfile.Emitted : CilProfile.Debug,
            artifact,
            artifact with { AssemblyPath = netWasm },
            directory)
        {
            Sources = emitted ? [] :
            [
                new(fixture.Name + ".cs", source, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)))),
                new("support/Part.cs", secondarySource, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(secondarySource)))),
            ],
        };
        var execution = new NetWasmExecution(
            System.Collections.Immutable.ImmutableDictionary<int, OracleObservation>.Empty,
            traceState == 0 ? null : trace,
            module,
            "module-sha",
            WasmTarget.Wasm32,
            true);

        var result = new CompilerFailureArtifactWriter(environment, replay, new CorpusSourceNamesVerifier()).Write(
            compilation,
            3,
            new(OracleObservationKind.Value, 4, null, 6),
            new(OracleObservationKind.Value, 5, null, 6),
            execution,
            "value mismatch");

        Assert.Equal(!emitted, File.Exists(Path.Combine(result, "fixture.cs")));
        using var caseDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(result, "failure.json")));
        Assert.Equal(fixture.CaseId, caseDocument.RootElement.GetProperty("CaseId").GetString());
        Assert.Equal(fixture.FeatureIds, caseDocument.RootElement.GetProperty("FeatureIds").EnumerateArray().Select(item => item.GetString()!));
        Assert.True(File.Exists(Path.Combine(result, "desktop.dll")));
        Assert.True(File.Exists(Path.Combine(result, "netwasm.dll")));
        Assert.True(File.Exists(Path.Combine(result, "application.wasm")));
        Assert.Equal(traceState >= 2, File.Exists(Path.Combine(result, "compiler-trace.txt")));
        Assert.Equal(generated, File.Exists(Path.Combine(result, "generated-cil.json")));
        Assert.Equal(traceState == 3, File.Exists(Path.Combine(
            result,
            "compiler-passes",
            "01-metadata.json")));
        if (traceState == 3)
        {
            Assert.True(File.Exists(Path.Combine(result, "compiler-passes", "nested", "detail.json")));
            var observations = File.ReadAllText(Path.Combine(result, "compiler-passes", "observations.json"));
            Assert.Contains("Desktop", observations);
            Assert.DoesNotContain('\r', observations);
        }
        else
        {
            Assert.False(Directory.Exists(Path.Combine(result, "compiler-passes")));
        }
        var failure = File.ReadAllText(Path.Combine(result, "failure.json"));
        Assert.DoesNotContain('\r', failure);
        using var document = JsonDocument.Parse(failure);
        Assert.Equal("value mismatch", document.RootElement.GetProperty("Reason").GetString());
        Assert.Equal(emitted ? "Emitted" : "Debug", document.RootElement.GetProperty("Profile").GetString());
        Assert.Equal(fixture.ReplayTestMethod, Assert.Single(replay.Methods));
        Assert.Equal(fixture.ReplayTestMethod, document.RootElement.GetProperty("ReplayTestMethod").GetString());
        Assert.Equal(hasReplayIdentity ? "test-method-filter" : "unavailable", document.RootElement.GetProperty("ReplayScope").GetString());
        Assert.Equal(fixture.Matrix?.CellId, document.RootElement.GetProperty("ReplayCell").GetString());
        Assert.Equal(fixture.Matrix?.Profile.ToString(), document.RootElement.GetProperty("MatrixProfile").GetString());
        Assert.Equal((fixture.Matrix?.CellId, fixture.ReplayInput, fixture.Matrix?.Profile), Assert.Single(replay.Selections));
        Assert.Equal(hasReplayIdentity ? "exact replay command" : null, document.RootElement.GetProperty("Reproduce").GetString());
        Assert.Equal(compilation.Sources.Length, document.RootElement.GetProperty("SourceFiles").GetArrayLength());
        foreach (var recorded in document.RootElement.GetProperty("SourceFiles").EnumerateArray())
        {
            var original = Assert.Single(compilation.Sources, item => item.Name == recorded.GetProperty("Name").GetString());
            var copy = Path.Combine(result, recorded.GetProperty("Path").GetString()!);
            Assert.Equal(File.ReadAllBytes(original.Path), File.ReadAllBytes(copy));
            Assert.Equal(original.Sha256, recorded.GetProperty("Sha256").GetString());
        }
        var second = new CompilerFailureArtifactWriter(environment, replay, new CorpusSourceNamesVerifier()).Write(
            compilation, 3, new(OracleObservationKind.Value, 4, null, 6),
            new(OracleObservationKind.Value, 7, null, 6), execution with { Target = WasmTarget.Wasm64 }, "second mismatch");
        Assert.NotEqual(result, second);
        Assert.Equal(failure, File.ReadAllText(Path.Combine(result, "failure.json")));
        using var secondDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(second, "failure.json")));
        Assert.Equal("second mismatch", secondDocument.RootElement.GetProperty("Reason").GetString());
        Assert.Equal(7, secondDocument.RootElement.GetProperty("NetWasm").GetProperty("Value").GetInt32());
    }

    [Fact]
    public void WritePreservesReplayFailureBeforeCreatingArtifacts()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var directory = CorrectnessTestAssets.CreateDirectory();
        var failure = new ArgumentException("invalid replay identity");
        var replay = new RecordingReplayFormatter(null, failure);
        var artifact = new CorpusArtifact("missing.dll", "", "sha", "", "sdk", []);
        var compilation = new CorpusCompilation(
            CorrectnessTestAssets.CreateFixture("ReplayFailure") with { ReplayTestMethod = "invalid" },
            CilProfile.Emitted, artifact, artifact, directory);
        var observation = new OracleObservation(OracleObservationKind.Value, 42, null, 0);
        var execution = new NetWasmExecution(
            System.Collections.Immutable.ImmutableDictionary<int, OracleObservation>.Empty,
            null, "missing.wasm", "sha", WasmTarget.Wasm32, true);
        var writer = Assert.IsAssignableFrom<ICompilerFailureArtifactWriter>(
            new CompilerFailureArtifactWriter(environment, replay, new CorpusSourceNamesVerifier()));
        try
        {
            Assert.Same(failure, Assert.Throws<ArgumentException>(() =>
                writer.Write(compilation, 0, observation, observation, execution, "mismatch")));
            Assert.Equal("invalid", Assert.Single(replay.Methods));
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    private sealed class RecordingReplayFormatter(string? command, Exception? failure = null) : ICorpusReplayCommandFormatter
    {
        public List<string?> Methods { get; } = [];
        public List<(string? Cell, int? Input, CorpusMatrixProfile? Profile)> Selections { get; } = [];

        public string? Format(string? testMethod, string? cellId = null, int? input = null, CorpusMatrixProfile? profile = null)
        {
            Methods.Add(testMethod);
            Selections.Add((cellId, input, profile));
            if (failure is not null)
            {
                throw failure;
            }
            return command;
        }
    }
}
