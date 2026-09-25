using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCaseFixtureFactoryTests
{
    [Fact]
    public void IndependentDesktopEntryDoesNotReplaceTargetEntry()
    {
        var manifest = CorpusCaseContractData.Manifest with { DesktopEntryMethod = "ExactOracle" };

        var fixture = Factory(new Boundaries()).Create(manifest, "Release-Wasm32-Direct");

        Assert.Equal("ExactOracle", fixture.DesktopEntryMethod);
        Assert.Equal(manifest.EntryMethod, fixture.WasmEntryMethod);
    }

    [Fact]
    public void ExplicitBackendSurvivesExactCellAndInputReplay()
    {
        var boundary = new Boundaries();
        var manifest = CorpusCaseContractData.Manifest with { ExecutionBackend = CorpusExecutionBackend.Linked };

        var fixture = Factory(boundary).Create(manifest, "Release-Wasm64-Direct-Linked", 0);

        Assert.Equal(new CorpusMatrixSelection(CorpusMatrixProfile.Extended, "Release-Wasm64-Direct-Linked",
            CorpusExecutionBackend.Linked), boundary.Selection);
        Assert.Same(boundary.Selection, fixture.Matrix);
        Assert.Equal(0, Assert.Single(fixture.Inputs));
        Assert.Equal(0, fixture.ReplayInput);
        Assert.Equal(42, fixture.ExpectedReturnValues[0]);
    }

    [Fact]
    public void MapsTheWholeDeclarationWithoutLoadingOrRewritingSource()
    {
        var boundary = new Boundaries();
        var manifest = CorpusCaseContractData.Manifest with
        {
            AllowUnsafe = true,
            RequiresReactor = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.Finalization,
            UsesTypedTrace = true,
            ExposesLegacyTrace = false,
            SupportsBatchedOracle = true,
            ReportAllMismatches = true,
            ReferenceAssemblyAliases = ImmutableDictionary<string, string>.Empty.Add("DesktopLib", "PortableLib"),
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Managed implementation.",
        };

        var fixture = Factory(boundary).Create(manifest, "Debug-Wasm64-Optimized");

        Assert.Same(manifest, boundary.Manifest);
        Assert.Equal(manifest.InputKind, boundary.InputKind);
        Assert.Equal(new CorpusMatrixSelection(CorpusMatrixProfile.Extended, "Debug-Wasm64-Optimized"), boundary.Selection);
        Assert.Equal(manifest.OracleMode, boundary.Mode);
        Assert.Same(fixture, boundary.Fixture);
        Assert.Equal(manifest.Name, fixture.Name);
        Assert.Equal(manifest.Namespace, fixture.Namespace);
        Assert.Equal("Tests.Numeric.EntryPoint", fixture.EntryType);
        Assert.Equal(string.Empty, fixture.Source);
        Assert.Empty(fixture.AdditionalSources);
        Assert.Equal(manifest.CaseId, fixture.CaseId);
        Assert.Equal(manifest.FeatureIds, fixture.FeatureIds);
        Assert.Equal(manifest.Inputs, fixture.Inputs);
        Assert.Equal(manifest.TestMethod, fixture.ReplayTestMethod);
        Assert.Null(fixture.ReplayInput);
        Assert.Equal(boundary.Selection, fixture.Matrix);
        Assert.True(fixture.AllowUnsafe);
        Assert.True(fixture.RequiresReactor);
        Assert.Equal(OracleRuntimeCapabilities.Finalization, fixture.RequiredRuntimeCapabilities);
        Assert.Null(fixture.ExpectedReturnValue);
        Assert.Equal(new KeyValuePair<int, int>(0, 42), Assert.Single(fixture.ExpectedReturnValues));
        Assert.Equal(new KeyValuePair<int, string>(1, "System.OverflowException"), Assert.Single(fixture.ExpectedExceptionTypes));
        Assert.True(fixture.UsesTypedTrace);
        Assert.False(fixture.ExposesLegacyTrace);
        Assert.True(fixture.SupportsBatchedOracle);
        Assert.True(fixture.ReportAllMismatches);
        Assert.Equal(manifest.EntryMethod, fixture.DesktopEntryMethod);
        Assert.Equal(manifest.EntryMethod, fixture.WasmEntryMethod);
        Assert.Equal(manifest.ReferencePaths, fixture.NetWasmReferencePaths);
        Assert.Same(manifest.ReferenceAssemblyAliases, fixture.ReferenceAssemblyAliases);
        Assert.Equal(manifest.OracleMode, fixture.OracleMode);
        Assert.Equal(manifest.SameSourceReason, fixture.SameSourceReason);
        Assert.Equal(["verify", "matrix", "mode", "policy"], boundary.Calls);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 0)]
    public void SelectedInputKeepsOnlyItsOwnIndependentContract(int input, int returns, int exceptions)
    {
        var fixture = Factory(new Boundaries()).Create(CorpusCaseContractData.Manifest, "Release-Wasm32-Direct", input);

        Assert.Equal(input, Assert.Single(fixture.Inputs));
        Assert.False(fixture.ReportAllMismatches);
        Assert.False(fixture.RequiresReactor);
        Assert.Equal(input, fixture.ReplayInput);
        Assert.Equal(returns, fixture.ExpectedReturnValues.Count);
        Assert.Equal(exceptions, fixture.ExpectedExceptionTypes.Count);
        if (returns == 1)
        {
            Assert.Equal(42, fixture.ExpectedReturnValues[input]);
        }
        if (exceptions == 1)
        {
            Assert.Equal("System.OverflowException", fixture.ExpectedExceptionTypes[input]);
        }
    }

    [Fact]
    public void UndeclaredInputFailsBeforeOraclePolicyLookup()
    {
        var boundary = new Boundaries();
        Assert.Throws<ArgumentException>(() => Factory(boundary).Create(CorpusCaseContractData.Manifest, "Release-Wasm32-Direct", 99));
        Assert.Equal(["verify", "matrix"], boundary.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingCellFailsBeforeAnyDependency(string? cell)
    {
        var boundary = new Boundaries();
        Assert.ThrowsAny<ArgumentException>(() => Factory(boundary).Create(CorpusCaseContractData.Manifest, cell!));
        Assert.Empty(boundary.Calls);
    }

    [Fact]
    public void MissingManifestFailsBeforeAnyDependency()
    {
        var boundary = new Boundaries();
        Assert.Throws<ArgumentNullException>(() => Factory(boundary).Create(null!, "Release-Wasm32-Direct"));
        Assert.Empty(boundary.Calls);
    }

    [Theory]
    [InlineData("verify", "verify")]
    [InlineData("matrix", "verify,matrix")]
    [InlineData("mode", "verify,matrix,mode")]
    [InlineData("policy", "verify,matrix,mode,policy")]
    public void RetainsFirstFailureWithoutReturningAPartialFixture(string failing, string calls)
    {
        var boundary = new Boundaries { Failing = failing };

        var failure = Assert.Throws<InvalidOperationException>(() => Factory(boundary).Create(CorpusCaseContractData.Manifest, "Release-Wasm32-Direct"));

        Assert.Same(boundary.Failure, failure);
        Assert.Equal(calls.Split(','), boundary.Calls);
    }

    private static ICorpusCaseFixtureFactory Factory(Boundaries boundaries) =>
        Assert.IsAssignableFrom<ICorpusCaseFixtureFactory>(new CorpusCaseFixtureFactory(boundaries, boundaries, boundaries));

    private sealed class Boundaries : ICorpusCaseManifestVerifier, ICorpusMatrixExpander, IOracleModePolicyRegistry, IOracleModePolicy
    {
        public List<string> Calls { get; } = [];
        public string? Failing { get; init; }
        public InvalidOperationException Failure { get; } = new("factory-boundary-failure");
        public CorpusCaseManifest? Manifest { get; private set; }
        public CorpusInputKind InputKind { get; private set; }
        public CorpusMatrixSelection? Selection { get; private set; }
        public CorpusFixture? Fixture { get; private set; }
        public OracleMode Mode { get; private set; }
        public bool SharesPortableExecutable => throw new NotSupportedException();
        public ImmutableDictionary<string, string> ReferenceAssemblyAliases => throw new NotSupportedException();

        public void Verify(CorpusCaseManifest manifest)
        {
            Record("verify");
            Manifest = manifest;
        }

        public ImmutableArray<CorpusMatrixCell> Expand(CorpusInputKind inputKind, CorpusMatrixSelection selection)
        {
            Record("matrix");
            InputKind = inputKind;
            Selection = selection;
            return [];
        }

        public IOracleModePolicy Get(OracleMode mode)
        {
            Record("mode");
            Mode = mode;
            return this;
        }

        public void Validate(CorpusFixture fixture)
        {
            Record("policy");
            Fixture = fixture;
        }

        public void ValidateCompilation(CorpusCompilation compilation) => throw new NotSupportedException();

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
