// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Analysis;

// Whether an unbounded virtual call on object with this many targets calls CoreLib's shared body for it
// (SharedObjectDispatch), as the preset's ObjectDispatchPolicy sets: reachability reaches the body for such a call and
// the program marks the call, from the one threshold.
internal interface IObjectDispatchSharing
{
    bool Shares(int targets);
}

internal sealed class ObjectDispatchSharing : IObjectDispatchSharing
{
    private readonly int _minimumTargets;

    public ObjectDispatchSharing(ObjectDispatchPolicy policy) => _minimumTargets = policy.MinimumTargets();

    public bool Shares(int targets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targets);
        return targets >= _minimumTargets;
    }
}
