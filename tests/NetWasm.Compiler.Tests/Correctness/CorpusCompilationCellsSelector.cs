using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCompilationCellsSelector
{
    ImmutableArray<CorpusMatrixCell> Select(CorpusCompilation compilation);
}

internal sealed class CorpusCompilationCellsSelector(ICorpusMatrixExpander matrices) : ICorpusCompilationCellsSelector
{
    public ImmutableArray<CorpusMatrixCell> Select(CorpusCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (compilation.Fixture.Matrix is not { } selection)
        {
            return default;
        }
        var inputKind = compilation.Profile == CilProfile.Emitted ? CorpusInputKind.Emitted : CorpusInputKind.CSharp;
        var cells = matrices.Expand(inputKind, selection)
            .Where(cell => cell.Profile == compilation.Profile).ToImmutableArray();
        if (cells.IsEmpty)
        {
            throw new ArgumentException("Compilation profile is outside the selected matrix.", nameof(compilation));
        }
        return cells;
    }
}
