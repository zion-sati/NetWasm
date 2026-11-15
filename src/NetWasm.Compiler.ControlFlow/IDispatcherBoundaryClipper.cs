// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IDispatcherBoundaryClipper
{
    ImmutableHashSet<int> Clip(
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? boundary);
}
