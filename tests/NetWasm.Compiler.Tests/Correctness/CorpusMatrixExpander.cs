using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal enum CorpusInputKind { CSharp, Emitted }
internal enum CorpusMatrixProfile { Fast, Family, Extended }
internal enum CorpusWasmForm { Direct, Optimized }
internal enum CorpusExecutionBackend { Simulated, Linked }

internal sealed record CorpusMatrixSelection(
    CorpusMatrixProfile Profile,
    string? CellId = null,
    CorpusExecutionBackend? Backend = null);

internal sealed record CorpusMatrixCell(
    CilProfile Profile,
    WasmTarget Target,
    CorpusWasmForm Form,
    CorpusExecutionBackend Backend = CorpusExecutionBackend.Simulated)
{
    public string Id => Backend == CorpusExecutionBackend.Simulated
        ? $"{Profile}-{Target}-{Form}"
        : $"{Profile}-{Target}-{Form}-{Backend}";
}

internal interface ICorpusMatrixExpander
{
    ImmutableArray<CorpusMatrixCell> Expand(CorpusInputKind inputKind, CorpusMatrixSelection selection);
}

internal sealed class CorpusMatrixExpander : ICorpusMatrixExpander
{
    private sealed record Definition(
        ImmutableArray<CilProfile> Profiles,
        ImmutableArray<WasmTarget> Targets,
        ImmutableArray<CorpusWasmForm> Forms,
        ImmutableArray<CorpusExecutionBackend> Backends);

    private static readonly ImmutableDictionary<CorpusInputKind, ImmutableArray<CilProfile>> InputProfiles =
        new Dictionary<CorpusInputKind, ImmutableArray<CilProfile>>
        {
            [CorpusInputKind.CSharp] = CilProfiles.Roslyn,
            [CorpusInputKind.Emitted] = [CilProfile.Emitted],
        }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<CorpusMatrixProfile, Definition> Definitions =
        new Dictionary<CorpusMatrixProfile, Definition>
        {
            [CorpusMatrixProfile.Fast] = new([CilProfile.Release, CilProfile.Emitted],
                [WasmTarget.Wasm32], [CorpusWasmForm.Direct],
                [CorpusExecutionBackend.Simulated]),
            [CorpusMatrixProfile.Family] = new([CilProfile.Debug, CilProfile.Release, CilProfile.Emitted],
                [WasmTarget.Wasm32, WasmTarget.Wasm64], [CorpusWasmForm.Direct],
                [CorpusExecutionBackend.Simulated]),
            [CorpusMatrixProfile.Extended] = new([CilProfile.Debug, CilProfile.Release, CilProfile.Emitted],
                [WasmTarget.Wasm32, WasmTarget.Wasm64],
                [CorpusWasmForm.Direct, CorpusWasmForm.Optimized],
                [CorpusExecutionBackend.Simulated, CorpusExecutionBackend.Linked]),
        }.ToImmutableDictionary();

    public ImmutableArray<CorpusMatrixCell> Expand(CorpusInputKind inputKind, CorpusMatrixSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!InputProfiles.TryGetValue(inputKind, out var inputProfiles))
        {
            throw new ArgumentOutOfRangeException(nameof(inputKind));
        }
        if (!Definitions.TryGetValue(selection.Profile, out var definition))
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }
        if (selection.Backend is { } declaredBackend && !Enum.IsDefined(declaredBackend))
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }
        var backends = selection.Backend is { } backendOverride
            ? [backendOverride]
            : definition.Backends;
        var cells = (from profile in definition.Profiles
                     where inputProfiles.Contains(profile)
                     from target in definition.Targets
                     from form in definition.Forms
                     from backend in backends
                     select new CorpusMatrixCell(profile, target, form, backend)).ToImmutableArray();
        if (selection.CellId is null)
        {
            return cells;
        }
        var selected = cells.SingleOrDefault(cell => cell.Id == selection.CellId)
            ?? throw new ArgumentException("Requested cell is outside the named matrix.", nameof(selection));
        return [selected];
    }
}
