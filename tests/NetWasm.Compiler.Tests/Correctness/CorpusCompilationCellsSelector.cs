using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCompilationCellsSelector
{
    ImmutableArray<CorpusMatrixCell> Select(CorpusCompilation compilation);
}

// NETWASM_CORPUS_BACKEND=linked runs every executed fixture on the linked
// runtime in place of the cells it selects; with NETWASM_CORPUS_GC_STRESS=1 that
// sweeps the corpus for references a collection frees while they are in use.
internal sealed record CorpusBackendOverride(CorpusExecutionBackend? Backend)
{
    public static CorpusBackendOverride Parse(string? value) => value switch
    {
        null or "" => new(Backend: null),
        "linked" => new(CorpusExecutionBackend.Linked),
        _ => throw new InvalidOperationException("NETWASM_CORPUS_BACKEND must be linked when it is set"),
    };
}

internal sealed class CorpusCompilationCellsSelector(
    ICorpusMatrixExpander matrices,
    CorpusBackendOverride backend) : ICorpusCompilationCellsSelector
{
    public ImmutableArray<CorpusMatrixCell> Select(CorpusCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (backend.Backend == CorpusExecutionBackend.Linked)
        {
            return [new(compilation.Profile, WasmTarget.Wasm32, CorpusWasmForm.Direct, CorpusExecutionBackend.Linked)];
        }
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
