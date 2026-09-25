using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCompilationCellsSelectorTests
{
    [Fact]
    public void NullCompilationFailsBeforeExpansion()
    {
        var matrices = new Matrices([]);
        var selector = Selector(matrices);

        var error = Assert.Throws<ArgumentNullException>(() => selector.Select(null!));

        Assert.Equal("compilation", error.ParamName);
        Assert.Empty(matrices.Requests);
    }

    [Fact]
    public void UnnamedMatrixPreservesDefaultWithoutExpansion()
    {
        var matrices = new Matrices([]);

        var result = Selector(matrices).Select(Compilation(CilProfile.Release, null));

        Assert.True(result.IsDefault);
        Assert.Empty(matrices.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionPreservesMatchingCellsAndTheirOrder(bool emitted)
    {
        var profile = emitted ? CilProfile.Emitted : CilProfile.Release;
        var first = new CorpusMatrixCell(profile, WasmTarget.Wasm64, CorpusWasmForm.Optimized);
        var second = new CorpusMatrixCell(profile, WasmTarget.Wasm32, CorpusWasmForm.Direct);
        var matrices = new Matrices([new(CilProfile.Debug, WasmTarget.Wasm32, CorpusWasmForm.Direct), first, second]);
        var selection = new CorpusMatrixSelection(CorpusMatrixProfile.Extended);

        var result = Selector(matrices).Select(Compilation(profile, selection));

        Assert.Collection(result, cell => Assert.Same(first, cell), cell => Assert.Same(second, cell));
        var request = Assert.Single(matrices.Requests);
        Assert.Equal(emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp, request.Kind);
        Assert.Same(selection, request.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoMatchingProfileFailsWithoutSubstitution(bool empty)
    {
        var matrices = new Matrices(empty ? [] : [new(CilProfile.Debug, WasmTarget.Wasm32, CorpusWasmForm.Direct)]);
        var compilation = Compilation(CilProfile.Release, new(CorpusMatrixProfile.Fast));

        var error = Assert.Throws<ArgumentException>(() => Selector(matrices).Select(compilation));

        Assert.Equal("compilation", error.ParamName);
        Assert.Contains("outside the selected matrix", error.Message);
        Assert.Single(matrices.Requests);
    }

    [Fact]
    public void ExpansionFailureIsPreservedWithoutRetry()
    {
        var failure = new InvalidOperationException("selection failed");
        var matrices = new Matrices([], failure);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Selector(matrices).Select(Compilation(CilProfile.Release, new(CorpusMatrixProfile.Fast))));

        Assert.Same(failure, error);
        Assert.Single(matrices.Requests);
    }

    private static ICorpusCompilationCellsSelector Selector(Matrices matrices) =>
        Assert.IsAssignableFrom<ICorpusCompilationCellsSelector>(new CorpusCompilationCellsSelector(matrices));

    private static CorpusCompilation Compilation(CilProfile profile, CorpusMatrixSelection? selection)
    {
        var artifact = new CorpusArtifact("fixture.dll", "", "sha", "", "sdk", []);
        return new(new("Selection", "Selection", "source", [0]) { Matrix = selection }, profile, artifact, artifact, "in-memory");
    }

    private sealed class Matrices(ImmutableArray<CorpusMatrixCell> cells, Exception? failure = null) : ICorpusMatrixExpander
    {
        public List<(CorpusInputKind Kind, CorpusMatrixSelection Selection)> Requests { get; } = [];

        public ImmutableArray<CorpusMatrixCell> Expand(CorpusInputKind inputKind, CorpusMatrixSelection selection)
        {
            Requests.Add((inputKind, selection));
            if (failure is not null) throw failure;
            return cells;
        }
    }
}
