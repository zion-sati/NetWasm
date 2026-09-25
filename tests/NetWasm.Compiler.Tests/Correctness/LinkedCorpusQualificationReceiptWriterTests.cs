using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusQualificationReceiptWriterTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32, 0)]
    [InlineData(false, WasmTarget.Wasm64, 1)]
    [InlineData(true, WasmTarget.Wasm32, 1)]
    [InlineData(true, WasmTarget.Wasm64, 0)]
    public void WriteRetainsExactArtifactsAndBothObservationsWithoutClaimingSemanticSuccess(
        bool emitted, WasmTarget target, int form)
    {
        using var assets = new Assets(emitted);
        var execution = assets.Execution with { Target = target, Form = (CorpusWasmForm)form };
        var bundle = assets.Writer().Write(assets.Compilation, execution, assets.Expected);
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "receipt.json")));
        var data = receipt.RootElement;
        Assert.Equal("linked-execution", data.GetProperty("EvidenceKind").GetString());
        Assert.Equal(new CorpusMatrixCell(assets.Compilation.Profile, target,
            (CorpusWasmForm)form, CorpusExecutionBackend.Linked).Id, data.GetProperty("Cell").GetString());
        Assert.Equal("numeric.receipt", data.GetProperty("CaseId").GetString());
        var observation = Assert.Single(data.GetProperty("Observations").EnumerateArray());
        Assert.Equal(7, observation.GetProperty("Input").GetInt32());
        Assert.Equal(42, observation.GetProperty("Desktop").GetProperty("Value").GetInt32());
        Assert.Equal(43, observation.GetProperty("Linked").GetProperty("Value").GetInt32());
        var entries = data.GetProperty("Artifacts").EnumerateArray().ToArray();
        Assert.Equal(11, entries.Length);
        foreach (var entry in entries)
        {
            var name = entry.GetProperty("Name").GetString()!;
            Assert.Equal(entry.GetProperty("Sha256").GetString(),
                assets.Hash.Compute(Path.Combine(bundle, name)));
        }
        Assert.Equal(emitted, File.Exists(Path.Combine(bundle, "generated-cil.json")));
        Assert.Equal(!emitted, File.Exists(Path.Combine(bundle, "sources", "nested", "Input.cs")));
        var step = Assert.Single(data.GetProperty("BuildSteps").EnumerateArray());
        Assert.Equal("tool", step.GetProperty("FileName").GetString());
        Assert.Equal(0, step.GetProperty("ExitCode").GetInt32());
        Assert.False(step.TryGetProperty("StandardOutput", out _));
        Assert.DoesNotContain("private-process-output", File.ReadAllText(Path.Combine(bundle, "receipt.json")));
        Assert.False(File.Exists(Path.Combine(bundle, "receipt.pending")));

        var second = assets.Writer().Write(assets.Compilation, execution, assets.Expected);
        Assert.NotEqual(bundle, second);
        Directory.Delete(assets.RunRoot, recursive: true);
        Assert.All(entries, entry => Assert.Equal(entry.GetProperty("Sha256").GetString(),
            assets.Hash.Compute(Path.Combine(bundle, entry.GetProperty("Name").GetString()!))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("run-root")]
    [InlineData("run-child")]
    public void WriteRejectsInvalidDestinationBeforeAllocatingBundle(string? kind)
    {
        using var assets = new Assets();
        var destination = kind switch
        {
            "missing" => Path.Combine(assets.Root, "missing"),
            "run-root" => assets.RunRoot,
            "run-child" => assets.Compilation.Directory,
            _ => kind,
        };
        Assert.NotNull(Record.Exception(() => assets.Writer(destination: destination, explicitDestination: true)
            .Write(assets.Compilation, assets.Execution, assets.Expected)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
        Assert.False(Directory.Exists(Path.Combine(assets.Root, "missing")));
    }

    [Fact]
    public void WriteRejectsDirectoryLinkIntoDisposableRoot()
    {
        using var assets = new Assets();
        var link = Path.Combine(assets.Root, "receipt-link");
        Directory.CreateSymbolicLink(link, assets.Compilation.Directory);
        Assert.Throws<ArgumentException>(() => assets.Writer(destination: link)
            .Write(assets.Compilation, assets.Execution, assets.Expected));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WriteRejectsNullInputsBeforeArtifacts(int input)
    {
        using var assets = new Assets();
        Assert.Throws<ArgumentNullException>(() => assets.Writer().Write(
            input == 0 ? null! : assets.Compilation,
            input == 1 ? null! : assets.Execution,
            input == 2 ? null! : assets.Expected));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WriteRejectsAbsentOrFailedBuildEvidence(int kind)
    {
        using var assets = new Assets();
        var steps = assets.Execution.BuildSteps;
        var execution = assets.Execution with
        {
            BuildSteps = kind switch
            {
                0 => default,
                1 => [],
                _ => [steps[0] with { Result = steps[0].Result with { ExitCode = 1 } }],
            },
        };
        Assert.Throws<ArgumentException>(() => assets.Writer().Write(assets.Compilation, execution, assets.Expected));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void WriteRejectsMissingDuplicateOrMismatchedInputSets(int kind)
    {
        using var assets = new Assets();
        var compilation = assets.Compilation;
        var expected = assets.Expected;
        var execution = assets.Execution;
        if (kind <= 2)
            compilation = compilation with
            {
                Fixture = compilation.Fixture with
                { Inputs = kind switch { 0 => default, 1 => [], _ => [7, 7] } }
            };
        else if (kind == 3) expected = expected.Clear();
        else if (kind == 4) execution = execution with { Observations = expected.Clear() };
        else if (kind == 5) expected = expected.Remove(7).Add(8, expected[7]);
        else execution = execution with { Observations = expected.Remove(7).Add(8, expected[7]) };
        Assert.Throws<ArgumentException>(() => assets.Writer().Write(compilation, execution, expected));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
    }

    [Fact]
    public void WriteRejectsInvalidSourceNamesBeforeCopying()
    {
        using var assets = new Assets();
        var compilation = assets.Compilation with
        {
            Sources = [assets.Compilation.Sources[0] with { Name = "../escape.cs" }],
        };
        Assert.Throws<ArgumentException>(() => assets.Writer().Write(compilation, assets.Execution, assets.Expected));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteRejectsChangedOrMissingArtifactBeforePublishing(bool missing)
    {
        using var assets = new Assets();
        if (missing) File.Delete(assets.Execution.ModulePath + ".observation.response.json");
        else File.WriteAllText(assets.Execution.ModulePath, "changed");
        var failure = Record.Exception(() => assets.Writer().Write(assets.Compilation, assets.Execution, assets.Expected));
        if (missing) Assert.IsType<FileNotFoundException>(failure);
        else Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Receipts));
    }

    [Fact]
    public void WriteRetainsPartialBundleWhenCopiedBytesFailVerification()
    {
        using var assets = new Assets();
        var failure = Assert.Throws<InvalidOperationException>(() => assets.Writer(new ChangedCopyHash(assets.Hash))
            .Write(assets.Compilation, assets.Execution, assets.Expected));
        var partial = Assert.IsType<string>(failure.Data["IncompleteEvidenceDirectory"]);
        Assert.Equal(Path.GetFileName(partial), Path.GetFileName(Assert.Single(Directory.EnumerateDirectories(assets.Receipts))));
        Assert.True(File.Exists(Path.Combine(partial, "desktop.dll")));
        Assert.False(File.Exists(Path.Combine(partial, "receipt.json")));
        Assert.False(File.Exists(Path.Combine(partial, "receipt.pending")));
        Assert.True(Directory.Exists(assets.RunRoot));
    }

    private sealed class ChangedCopyHash(ICorpusArtifactFingerprint hash) : ICorpusArtifactFingerprint
    {
        public string Compute(string path) => Path.GetFileName(path) == "desktop.dll"
            ? "changed-copy" : hash.Compute(path);
    }

    private sealed class Assets : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("netwasm-receipt-contract-").FullName;
        public string RunRoot { get; }
        public string Receipts { get; }
        public CorpusArtifactFingerprint Hash { get; } = new();
        public CorpusCompilation Compilation { get; }
        public LinkedCorpusExecution Execution { get; }
        public ImmutableDictionary<int, OracleObservation> Expected { get; } =
            ImmutableDictionary<int, OracleObservation>.Empty.Add(7, new(OracleObservationKind.Value, 42, null, 0));

        public Assets(bool emitted = false)
        {
            RunRoot = Directory.CreateDirectory(Path.Combine(Root, "run")).FullName;
            Receipts = Directory.CreateDirectory(Path.Combine(Root, "receipts")).FullName;
            var directory = Directory.CreateDirectory(Path.Combine(RunRoot, "Release")).FullName;
            string Create(string name)
            {
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, name);
                return path;
            }
            var pe = Create("input.dll");
            var artifact = new CorpusArtifact(pe, "", Hash.Compute(pe), "", "sdk", ["option"]);
            Compilation = new(new("Receipt", "Receipt", "source", [7]) { CaseId = "numeric.receipt" },
                emitted ? CilProfile.Emitted : CilProfile.Release, artifact, artifact, directory);
            if (emitted) Create("generated-cil.json");
            else
            {
                var source = Create("Input.cs");
                Compilation = Compilation with { Sources = [new("nested/Input.cs", source, Hash.Compute(source))] };
            }
            var app = Create("application.wasm");
            var layout = Create("layout.json");
            var manifest = Create("manifest.json");
            var linked = Create("linked.wasm");
            Create("application.wasm.request.json");
            Create("application.wasm.response.json");
            Create("linked.wasm.observation.request.json");
            Create("linked.wasm.observation.response.json");
            Execution = new(Expected.SetItem(7, new(OracleObservationKind.Value, 43, null, 0)),
                WasmTarget.Wasm32, CorpusWasmForm.Direct, app, Hash.Compute(app),
                layout, Hash.Compute(layout), manifest, Hash.Compute(manifest), linked, Hash.Compute(linked),
                [new(new(LinkedCorpusBuildStage.ValidateMerged, new("tool", ["argument"], TimeSpan.FromSeconds(1))),
                    new(QualifiedProcessCompletion.Exited, 0, "private-process-output", "private-process-output", TimeSpan.FromSeconds(1)))]);
        }

        public ILinkedCorpusQualificationReceiptWriter Writer(ICorpusArtifactFingerprint? hash = null,
            string? destination = null, bool explicitDestination = false) =>
            Assert.IsAssignableFrom<ILinkedCorpusQualificationReceiptWriter>(
                new LinkedCorpusQualificationReceiptWriter(new LinkedCorpusReceiptDestinationValidator(
                    new(explicitDestination ? destination : destination ?? Receipts)),
                    hash ?? Hash, new CorpusSourceNamesVerifier()));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
