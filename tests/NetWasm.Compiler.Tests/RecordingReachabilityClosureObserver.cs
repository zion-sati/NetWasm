// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

internal sealed class RecordingReachabilityClosureObserver : IReachabilityClosureObserver
{
    private readonly List<ReachabilityClosureObservation> _observations = [];

    public IReadOnlyList<ReachabilityClosureObservation> Observations => _observations;

    public void Observe(ReachabilityClosureObservation observation)
    {
        _observations.Add(observation);
    }
}
