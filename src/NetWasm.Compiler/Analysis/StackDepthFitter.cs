// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// A rewritten body with its maximum stack raised to the depth its evaluation stack reaches, for a rewrite that may push
// past the old one (a dup that keeps a value for later, field locals loaded in place of a struct). Never lowered:
// emission sizes its evaluation stack locals from it, so a body the rewrite keeps within it is returned unchanged.
internal interface IStackDepthFitter
{
    CilMethodBody Fit(CilMethodBody body);
}

internal sealed class StackDepthFitter(IControlFlowGraphBuilder graphs, ITypedStackValidator stacks) : IStackDepthFitter
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Fit(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var depth = _stacks.Validate(_graphs.Build(body with { MaxStack = int.MaxValue })).MaximumStackDepth;
        return depth > body.MaxStack ? body with { MaxStack = depth } : body;
    }
}
