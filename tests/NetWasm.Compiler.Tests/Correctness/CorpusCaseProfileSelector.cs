namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCaseProfileSelector
{
    CorpusCaseManifest Select(CorpusCaseManifest manifest, CorpusMatrixProfile? profile);
}

internal sealed class CorpusCaseProfileSelector(ICorpusMatrixExpander matrices) : ICorpusCaseProfileSelector
{
    public CorpusCaseManifest Select(CorpusCaseManifest manifest, CorpusMatrixProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var selected = manifest with { MatrixProfile = profile ?? manifest.MatrixProfile };
        matrices.Expand(selected.InputKind, new(selected.MatrixProfile, Backend: selected.ExecutionBackend));
        return selected;
    }
}
