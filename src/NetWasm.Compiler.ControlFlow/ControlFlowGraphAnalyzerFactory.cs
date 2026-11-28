// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowGraphAnalyzerFactory : IControlFlowGraphAnalyzerFactory
{
    private readonly IControlFlowDominanceAnalyzer _dominance;
    private readonly IControlFlowComponentAnalyzer _components;
    private readonly IControlFlowNaturalLoopAnalyzer _naturalLoops;

    public ControlFlowGraphAnalyzerFactory()
        : this(
            new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder()),
            new ControlFlowComponentAnalyzer(),
            new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer(new DominatorTreeBuilder())))
    {
    }

    public ControlFlowGraphAnalyzerFactory(
        IControlFlowDominanceAnalyzer dominance,
        IControlFlowComponentAnalyzer components,
        IControlFlowNaturalLoopAnalyzer naturalLoops)
    {
        _dominance = dominance ?? throw new ArgumentNullException(nameof(dominance));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _naturalLoops = naturalLoops ??
            throw new ArgumentNullException(nameof(naturalLoops));
    }

    public IControlFlowGraphAnalyzer Create() =>
        new ControlFlowGraphAnalyzer(
            _dominance,
            _components,
            _naturalLoops);
}
