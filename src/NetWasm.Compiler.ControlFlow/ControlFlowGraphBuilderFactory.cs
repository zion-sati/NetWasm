// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowGraphBuilderFactory : IControlFlowGraphBuilderFactory
{
    private readonly ICilExceptionRegionValidator _exceptionRegions;

    public ControlFlowGraphBuilderFactory()
        : this(new CilExceptionRegionValidator())
    {
    }

    public ControlFlowGraphBuilderFactory(ICilExceptionRegionValidator exceptionRegions)
    {
        _exceptionRegions = exceptionRegions ??
            throw new ArgumentNullException(nameof(exceptionRegions));
    }

    public IControlFlowGraphBuilder Create() =>
        new CilControlFlowGraphBuilder(_exceptionRegions);
}
