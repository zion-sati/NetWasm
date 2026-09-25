using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCaseManifestVerifierTests
{
    [Fact]
    public void DeclaredBackendIsValidatedByTheMatrixBoundary()
    {
        var boundary = new Boundaries();
        var manifest = Valid() with { ExecutionBackend = CorpusExecutionBackend.Linked };

        Create(boundary).Verify(manifest);

        Assert.Equal(new CorpusMatrixSelection(manifest.MatrixProfile, Backend: CorpusExecutionBackend.Linked),
            boundary.Selection);
    }

    private static readonly ImmutableDictionary<string, CorpusCaseManifest> Invalid =
        new Dictionary<string, CorpusCaseManifest>
        {
            ["schema"] = Valid() with { SchemaVersion = 2 },
            ["null-id"] = Valid() with { CaseId = null! },
            ["empty-id"] = Valid() with { CaseId = "" },
            ["uppercase-id"] = Valid() with { CaseId = "Numeric" },
            ["filter-id"] = Valid() with { CaseId = "numeric|other" },
            ["features-default"] = Valid() with { FeatureIds = default },
            ["features-empty"] = Valid() with { FeatureIds = [] },
            ["features-duplicate"] = Valid() with { FeatureIds = ["S06", "S06"] },
            ["features-null"] = Valid() with { FeatureIds = [null!] },
            ["features-unknown"] = Valid() with { FeatureIds = ["S99"] },
            ["name-null"] = Valid() with { Name = null! },
            ["name-invalid"] = Valid() with { Name = "../escape" },
            ["namespace-null"] = Valid() with { Namespace = null! },
            ["namespace-invalid"] = Valid() with { Namespace = "Has Space" },
            ["entry-null"] = Valid() with { EntryMethod = null! },
            ["entry-invalid"] = Valid() with { EntryMethod = "Run()" },
            ["desktop-entry-empty"] = Valid() with { DesktopEntryMethod = "" },
            ["desktop-entry-invalid"] = Valid() with { DesktopEntryMethod = "Oracle()" },
            ["test-null"] = Valid() with { TestMethod = null! },
            ["test-blank"] = Valid() with { TestMethod = " " },
            ["kind-unknown"] = Valid() with { InputKind = (CorpusInputKind)99 },
            ["sources-default"] = Valid() with { SourceFiles = default },
            ["emitted-has-source"] = Valid() with { InputKind = CorpusInputKind.Emitted },
            ["emitted-same-source"] = Valid() with
            {
                InputKind = CorpusInputKind.Emitted,
                SourceFiles = [],
                OracleMode = OracleMode.SameSource,
                SameSourceReason = "A reason.",
            },
            ["mode-unknown"] = Valid() with { OracleMode = (OracleMode)99 },
            ["mode-frozen"] = Valid() with { OracleMode = OracleMode.FrozenDesktop },
            ["same-il-reason"] = Valid() with { SameSourceReason = "A reason." },
            ["same-source-no-reason"] = Valid() with { OracleMode = OracleMode.SameSource },
            ["same-source-blank-reason"] = Valid() with { OracleMode = OracleMode.SameSource, SameSourceReason = " " },
            ["capability-unknown"] = Valid() with { RequiredRuntimeCapabilities = (OracleRuntimeCapabilities)8 },
            ["capability-negative"] = Valid() with { RequiredRuntimeCapabilities = (OracleRuntimeCapabilities)(-1) },
            ["references-default"] = Valid() with { ReferencePaths = default },
            ["references-null"] = Valid() with { ReferencePaths = [null!] },
            ["references-blank"] = Valid() with { ReferencePaths = [" "] },
            ["references-duplicate"] = Valid() with { ReferencePaths = ["x.dll", "x.dll"] },
            ["aliases-null"] = Valid() with { ReferenceAssemblyAliases = null! },
            ["aliases-empty-key"] = Valid() with { ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add(" ", "NetWasm.CoreLib") },
            ["aliases-empty-value"] = Valid() with { ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", " ") },
            ["inputs-default"] = Valid() with { Inputs = default },
            ["inputs-empty"] = Valid() with { Inputs = [] },
            ["inputs-duplicate"] = Valid() with { Inputs = [0, 0] },
            ["expectations-default"] = Valid() with { Expectations = default },
            ["expectation-null"] = Valid() with { Expectations = [null!] },
            ["expectation-undeclared"] = Valid() with { Expectations = [new(99, 42, null)] },
            ["expectation-duplicate"] = Valid() with { Expectations = [new(0, 42, null), new(0, 42, null)] },
            ["expectation-neither"] = Valid() with { Expectations = [new(0, null, null)] },
            ["expectation-both"] = Valid() with { Expectations = [new(0, 42, "System.Exception")] },
            ["exception-empty"] = Valid() with { Expectations = [new(0, null, "")] },
            ["exception-whitespace"] = Valid() with { Expectations = [new(0, null, " ")] },
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public static TheoryData<string> InvalidDeclarations => new(Invalid.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(InvalidDeclarations))]
    public void RejectsInconsistentDeclaration(string name)
    {
        var boundary = new Boundaries();
        var verifier = Create(boundary);

        Assert.ThrowsAny<ArgumentException>(() => verifier.Verify(Invalid[name]));
    }

    [Fact]
    public void RejectsNullBeforeCallingCollaborators()
    {
        var boundary = new Boundaries();
        Assert.Throws<ArgumentNullException>(() => Create(boundary).Verify(null!));
        Assert.Empty(boundary.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatesDeclaredNamesMatrixAndReplayWithoutExecutingAnything(bool emitted)
    {
        var boundary = new Boundaries();
        var manifest = emitted
            ? Valid() with { InputKind = CorpusInputKind.Emitted, SourceFiles = [] }
            : Valid();

        Create(boundary).Verify(manifest);

        string[] expectedCalls = emitted ? ["replay", "matrix"] : ["replay", "sources", "matrix"];
        Assert.Equal(expectedCalls, boundary.Calls);
        Assert.Equal(manifest.TestMethod, boundary.TestMethod);
        Assert.Null(boundary.CellId);
        Assert.Null(boundary.ReplayInput);
        Assert.Equal(manifest.InputKind, boundary.InputKind);
        Assert.Equal(new CorpusMatrixSelection(manifest.MatrixProfile), boundary.Selection);
        if (!emitted)
        {
            Assert.Equal(manifest.SourceFiles, boundary.SourceFiles);
        }
    }

    [Fact]
    public void AcceptsDeclaredCapabilitiesAndDoesNotClaimBackendSupport()
    {
        var boundary = new Boundaries();
        var manifest = Valid() with
        {
            FeatureIds = ["S01", "S06"],
            DesktopEntryMethod = "ExactOracle",
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Tests the managed implementation.",
            RequiredRuntimeCapabilities = (OracleRuntimeCapabilities)7,
            ReferencePaths = ["a.dll", "b.dll"],
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add("System.Private.CoreLib", "NetWasm.CoreLib"),
            Inputs = [int.MinValue, 0, int.MaxValue],
            Expectations = [new(int.MinValue, int.MaxValue, null), new(0, null, "Example.Outer+NestedException")],
            AllowUnsafe = true,
            SupportsBatchedOracle = true,
            ExposesLegacyTrace = false,
            UsesTypedTrace = true,
        };

        Create(boundary).Verify(manifest);

        Assert.Equal(new CorpusMatrixSelection(CorpusMatrixProfile.Extended), boundary.Selection);
        Assert.Equal(["replay", "sources", "matrix"], boundary.Calls);
    }

    [Fact]
    public void AllowsExplicitDifferentialOnlyExpectations()
    {
        var boundary = new Boundaries();
        Create(boundary).Verify(Valid() with { Expectations = [] });
        Assert.Equal(["replay", "sources", "matrix"], boundary.Calls);
    }

    [Theory]
    [InlineData("replay", "replay")]
    [InlineData("sources", "replay,sources")]
    [InlineData("matrix", "replay,sources,matrix")]
    public void PreservesFirstCollaboratorFailureAndDoesNotCallLaterDependencies(string failing, string calls)
    {
        var boundary = new Boundaries { Failing = failing };

        var failure = Assert.Throws<InvalidOperationException>(() => Create(boundary).Verify(Valid()));

        Assert.Same(boundary.Failure, failure);
        Assert.Equal(calls.Split(','), boundary.Calls);
    }

    [Theory]
    [InlineData("invalid-replay")]
    [InlineData("invalid-source")]
    [InlineData("empty-source")]
    [InlineData("duplicate-source")]
    [InlineData("invalid-matrix")]
    [InlineData("invalid-backend")]
    public void CompositionUsesTheExistingBoundaryPolicies(string defect)
    {
        var manifest = defect switch
        {
            "invalid-replay" => Valid() with { TestMethod = "Tests.Run|Others" },
            "invalid-source" => Valid() with { SourceFiles = ["../escape.cs"] },
            "empty-source" => Valid() with { SourceFiles = [] },
            "duplicate-source" => Valid() with { SourceFiles = ["Entry.cs", "entry.cs"] },
            "invalid-matrix" => Valid() with { MatrixProfile = (CorpusMatrixProfile)99 },
            "invalid-backend" => Valid() with { ExecutionBackend = (CorpusExecutionBackend)99 },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        var verifier = Assert.IsAssignableFrom<ICorpusCaseManifestVerifier>(new CorpusCaseManifestVerifier(
            Features(), new CorpusSourceNamesVerifier(), new CorpusMatrixExpander(), new CorpusReplayCommandFormatter()));

        Assert.ThrowsAny<ArgumentException>(() => verifier.Verify(manifest));
    }

    private static CorpusCaseManifest Valid() => new(
        1, "numeric.boundaries-1", ["S06"], CorpusInputKind.CSharp, ["numeric/Entry.cs.txt"],
        "NumericBoundaries", "NetWasm.Correctness.Numeric", "Run", [0], [new(0, 42, null)], [],
        OracleMode.SameIl, CorpusMatrixProfile.Extended, "NetWasm.Compiler.Tests.NumericTests.Boundaries");

    private static CorpusFeatureCatalog Features() => new(ImmutableHashSet.Create(StringComparer.Ordinal, "S01", "S06"));

    private static ICorpusCaseManifestVerifier Create(Boundaries boundaries) =>
        Assert.IsAssignableFrom<ICorpusCaseManifestVerifier>(
            new CorpusCaseManifestVerifier(Features(), boundaries, boundaries, boundaries));

    private sealed class Boundaries : ICorpusSourceNamesVerifier, ICorpusMatrixExpander, ICorpusReplayCommandFormatter
    {
        public List<string> Calls { get; } = [];
        public string? Failing { get; init; }
        public InvalidOperationException Failure { get; } = new("manifest-boundary-failure");
        public ImmutableArray<string> SourceFiles { get; private set; }
        public CorpusInputKind InputKind { get; private set; }
        public CorpusMatrixSelection? Selection { get; private set; }
        public string? TestMethod { get; private set; }
        public string? CellId { get; private set; }
        public int? ReplayInput { get; private set; }

        public void Verify(ImmutableArray<string> sourceFiles)
        {
            Record("sources");
            SourceFiles = sourceFiles;
        }

        public ImmutableArray<CorpusMatrixCell> Expand(CorpusInputKind inputKind, CorpusMatrixSelection selection)
        {
            Record("matrix");
            InputKind = inputKind;
            Selection = selection;
            return [];
        }

        public string? Format(string? testMethod, string? cellId = null, int? input = null, CorpusMatrixProfile? profile = null)
        {
            Assert.Null(profile);
            Record("replay");
            TestMethod = testMethod;
            CellId = cellId;
            ReplayInput = input;
            return "exact-replay-command";
        }

        private void Record(string call)
        {
            Calls.Add(call);
            if (call == Failing)
            {
                throw Failure;
            }
        }
    }
}
