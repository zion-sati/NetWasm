namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusMatrixExpanderTests
{
    [Theory]
    [InlineData(false, 0, "Release-Wasm32-Direct")]
    [InlineData(true, 0, "Emitted-Wasm32-Direct")]
    [InlineData(false, 1, "Debug-Wasm32-Direct,Debug-Wasm64-Direct,Release-Wasm32-Direct,Release-Wasm64-Direct")]
    [InlineData(true, 1, "Emitted-Wasm32-Direct,Emitted-Wasm64-Direct")]
    [InlineData(false, 2, "Debug-Wasm32-Direct,Debug-Wasm32-Optimized,Debug-Wasm64-Direct,Debug-Wasm64-Optimized,Release-Wasm32-Direct,Release-Wasm32-Optimized,Release-Wasm64-Direct,Release-Wasm64-Optimized")]
    [InlineData(true, 2, "Emitted-Wasm32-Direct,Emitted-Wasm32-Optimized,Emitted-Wasm64-Direct,Emitted-Wasm64-Optimized")]
    public void ExplicitBackendPreservesEveryOtherDimensionAndExactReplay(bool emitted, int profile, string expected)
    {
        var expander = Assert.IsAssignableFrom<ICorpusMatrixExpander>(new CorpusMatrixExpander());
        var kind = emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp;
        foreach (var backend in new[] { CorpusExecutionBackend.Simulated, CorpusExecutionBackend.Linked })
        {
            var selection = new CorpusMatrixSelection((CorpusMatrixProfile)profile, Backend: backend);
            var cells = expander.Expand(kind, selection);
            var suffix = backend == CorpusExecutionBackend.Linked ? "-Linked" : "";

            Assert.Equal(expected.Split(',').Select(id => id + suffix), cells.Select(cell => cell.Id));
            Assert.All(cells, cell => Assert.Equal(backend, cell.Backend));
            foreach (var cell in cells)
                Assert.Equal<CorpusMatrixCell>([cell], expander.Expand(kind, selection with { CellId = cell.Id }));

            var contradictory = cells[0].Id.EndsWith("-Linked", StringComparison.Ordinal)
                ? cells[0].Id[..^7]
                : cells[0].Id + "-Linked";
            Assert.Throws<ArgumentException>(() => expander.Expand(kind, selection with { CellId = contradictory }));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UnknownBackendFailsWithoutProducingCells(int backend)
    {
        var expander = Assert.IsAssignableFrom<ICorpusMatrixExpander>(new CorpusMatrixExpander());

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => expander.Expand(
            CorpusInputKind.CSharp, new(CorpusMatrixProfile.Extended, Backend: (CorpusExecutionBackend)backend)));

        Assert.Equal("selection", error.ParamName);
    }

    [Theory]
    [InlineData(false, 0, "Release-Wasm32-Direct")]
    [InlineData(true, 0, "Emitted-Wasm32-Direct")]
    [InlineData(false, 1, "Debug-Wasm32-Direct,Debug-Wasm64-Direct,Release-Wasm32-Direct,Release-Wasm64-Direct")]
    [InlineData(true, 1, "Emitted-Wasm32-Direct,Emitted-Wasm64-Direct")]
    [InlineData(false, 2, "Debug-Wasm32-Direct,Debug-Wasm32-Direct-Linked,Debug-Wasm32-Optimized,Debug-Wasm32-Optimized-Linked,Debug-Wasm64-Direct,Debug-Wasm64-Direct-Linked,Debug-Wasm64-Optimized,Debug-Wasm64-Optimized-Linked,Release-Wasm32-Direct,Release-Wasm32-Direct-Linked,Release-Wasm32-Optimized,Release-Wasm32-Optimized-Linked,Release-Wasm64-Direct,Release-Wasm64-Direct-Linked,Release-Wasm64-Optimized,Release-Wasm64-Optimized-Linked")]
    [InlineData(true, 2, "Emitted-Wasm32-Direct,Emitted-Wasm32-Direct-Linked,Emitted-Wasm32-Optimized,Emitted-Wasm32-Optimized-Linked,Emitted-Wasm64-Direct,Emitted-Wasm64-Direct-Linked,Emitted-Wasm64-Optimized,Emitted-Wasm64-Optimized-Linked")]
    public void ExpandProducesTheExactNamedMatrix(bool emitted, int profile, string expected)
    {
        var expander = Assert.IsAssignableFrom<ICorpusMatrixExpander>(new CorpusMatrixExpander());

        var cells = expander.Expand(emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp,
            new((CorpusMatrixProfile)profile));

        Assert.Equal(expected.Split(','), cells.Select(cell => cell.Id));
        Assert.Equal(cells.Length, cells.Distinct().Count());
        foreach (var cell in cells)
        {
            Assert.Equal<CorpusMatrixCell>([cell], expander.Expand(
                emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp,
                new((CorpusMatrixProfile)profile, cell.Id)));
        }
    }

    [Theory]
    [InlineData(false, 0, "Debug-Wasm32-Direct")]
    [InlineData(false, 1, "Release-Wasm64-Optimized")]
    [InlineData(false, 2, "Emitted-Wasm32-Direct")]
    [InlineData(true, 2, "Release-Wasm32-Direct")]
    [InlineData(true, 2, "Emitted-Wasm128-Direct")]
    [InlineData(true, 2, "emitted-wasm32-direct")]
    [InlineData(false, 0, "Release-Wasm32-Direct-Linked")]
    [InlineData(true, 2, "")]
    [InlineData(true, 2, " ")]
    public void ExpandRejectsCellsOutsideTheRequestedProfile(bool emitted, int profile, string cell)
    {
        var expander = Assert.IsAssignableFrom<ICorpusMatrixExpander>(new CorpusMatrixExpander());

        var error = Assert.Throws<ArgumentException>(() => expander.Expand(
            emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp, new((CorpusMatrixProfile)profile, cell)));

        Assert.Equal("selection", error.ParamName);
    }

    [Theory]
    [InlineData(-1, 0, "inputKind")]
    [InlineData(99, 0, "inputKind")]
    [InlineData(0, -1, "selection")]
    [InlineData(0, 99, "selection")]
    public void ExpandRejectsUnknownKindsAndProfiles(int kind, int profile, string parameter)
    {
        var expander = Assert.IsAssignableFrom<ICorpusMatrixExpander>(new CorpusMatrixExpander());

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => expander.Expand(
            (CorpusInputKind)kind, new((CorpusMatrixProfile)profile)));

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void ExpandRejectsMissingSelection()
    {
        var expander = Assert.IsAssignableFrom<ICorpusMatrixExpander>(new CorpusMatrixExpander());

        Assert.Throws<ArgumentNullException>(() => expander.Expand(CorpusInputKind.CSharp, null!));
    }
}
