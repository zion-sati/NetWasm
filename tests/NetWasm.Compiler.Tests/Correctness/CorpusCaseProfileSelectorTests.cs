using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCaseProfileSelectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitBackendSurvivesEveryProfileOverride(int? profile)
    {
        var matrices = new RecordingMatrices();
        var selector = Assert.IsAssignableFrom<ICorpusCaseProfileSelector>(new CorpusCaseProfileSelector(matrices));
        var manifest = CorpusCaseContractData.Manifest with { ExecutionBackend = CorpusExecutionBackend.Linked };

        var selected = selector.Select(manifest, profile is { } value ? (CorpusMatrixProfile)value : null);

        Assert.Equal(CorpusExecutionBackend.Linked, selected.ExecutionBackend);
        Assert.Equal(manifest, selected with { MatrixProfile = manifest.MatrixProfile });
        Assert.Equal(new CorpusMatrixSelection(selected.MatrixProfile, Backend: CorpusExecutionBackend.Linked),
            Assert.Single(matrices.Calls).Selection);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void SelectsTheRequestedMatrixWithoutReducingItToTheDefault(int? requested, int expected)
    {
        var matrices = new RecordingMatrices();
        var selector = Assert.IsAssignableFrom<ICorpusCaseProfileSelector>(new CorpusCaseProfileSelector(matrices));
        var manifest = CorpusCaseContractData.Manifest with { MatrixProfile = CorpusMatrixProfile.Fast };

        var selected = selector.Select(manifest, requested is { } profile ? (CorpusMatrixProfile)profile : null);

        Assert.Equal((CorpusMatrixProfile)expected, selected.MatrixProfile);
        Assert.Equal(manifest, selected with { MatrixProfile = manifest.MatrixProfile });
        Assert.Equal(CorpusMatrixProfile.Fast, manifest.MatrixProfile);
        Assert.Equal((manifest.InputKind, new CorpusMatrixSelection((CorpusMatrixProfile)expected)), Assert.Single(matrices.Calls));
    }

    [Fact]
    public void NullManifestFailsBeforeExpansion()
    {
        var matrices = new RecordingMatrices();
        var selector = Assert.IsAssignableFrom<ICorpusCaseProfileSelector>(new CorpusCaseProfileSelector(matrices));

        Assert.Throws<ArgumentNullException>(() => selector.Select(null!, null));
        Assert.Empty(matrices.Calls);
    }

    [Fact]
    public void MatrixFailurePropagatesWithoutRetry()
    {
        var matrices = new RecordingMatrices { Fail = true };
        var selector = Assert.IsAssignableFrom<ICorpusCaseProfileSelector>(new CorpusCaseProfileSelector(matrices));

        var failure = Assert.Throws<InvalidOperationException>(() => selector.Select(CorpusCaseContractData.Manifest, (CorpusMatrixProfile)99));

        Assert.Same(matrices.Failure, failure);
        Assert.Equal(new CorpusMatrixSelection((CorpusMatrixProfile)99), Assert.Single(matrices.Calls).Selection);
    }

    private sealed class RecordingMatrices : ICorpusMatrixExpander
    {
        public List<(CorpusInputKind Kind, CorpusMatrixSelection Selection)> Calls { get; } = [];
        public bool Fail { get; init; }
        public InvalidOperationException Failure { get; } = new("profile-selection-failure");

        public ImmutableArray<CorpusMatrixCell> Expand(CorpusInputKind inputKind, CorpusMatrixSelection selection)
        {
            Calls.Add((inputKind, selection));
            if (Fail)
            {
                throw Failure;
            }
            return [];
        }
    }
}
