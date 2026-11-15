// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

internal sealed class RecordingReachableMethodBatchObserver : IReachableMethodBatchObserver
{
    private readonly List<ReachableMethodBatchObservation> _observations = [];

    public IReadOnlyList<ReachableMethodBatchObservation> Observations => _observations;

    public void Observe(ReachableMethodBatchObservation observation)
    {
        _observations.Add(observation);
    }
}
