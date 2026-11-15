// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
