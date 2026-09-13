using System;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowGraphAnalyzerFactory : IControlFlowGraphAnalyzerFactory
{
    private readonly IControlFlowDominanceAnalyzer _dominance;
    private readonly IControlFlowPostDominanceAnalyzer _postDominance;
    private readonly IControlFlowComponentAnalyzer _components;
    private readonly IControlFlowNaturalLoopAnalyzer _naturalLoops;

    public ControlFlowGraphAnalyzerFactory()
        : this(
            new ControlFlowDominanceAnalyzer(),
            new ControlFlowPostDominanceAnalyzer(),
            new ControlFlowComponentAnalyzer(),
            new ControlFlowNaturalLoopAnalyzer(new ControlFlowDominanceAnalyzer()))
    {
    }

    public ControlFlowGraphAnalyzerFactory(
        IControlFlowDominanceAnalyzer dominance,
        IControlFlowPostDominanceAnalyzer postDominance,
        IControlFlowComponentAnalyzer components,
        IControlFlowNaturalLoopAnalyzer naturalLoops)
    {
        _dominance = dominance ?? throw new ArgumentNullException(nameof(dominance));
        _postDominance = postDominance ??
            throw new ArgumentNullException(nameof(postDominance));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _naturalLoops = naturalLoops ??
            throw new ArgumentNullException(nameof(naturalLoops));
    }

    public IControlFlowGraphAnalyzer Create() =>
        new ControlFlowGraphAnalyzer(
            _dominance,
            _postDominance,
            _components,
            _naturalLoops);
}
