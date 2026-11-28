// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class DispatcherBoundaryClipper : IDispatcherBoundaryClipper
{
    public ImmutableHashSet<int> Clip(
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? boundary)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(allowed);
        var clipped = component.Within(allowed);
        return boundary is int stop ? clipped.Remove(stop) : clipped;
    }
}
