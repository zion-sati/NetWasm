using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowGraphAnalyzer(
    IControlFlowDominanceAnalyzer dominance,
    IControlFlowPostDominanceAnalyzer postDominance,
    IControlFlowComponentAnalyzer components,
    IControlFlowNaturalLoopAnalyzer naturalLoops) : IControlFlowGraphAnalyzer
{
    private readonly IControlFlowDominanceAnalyzer _dominance =
        dominance ?? throw new ArgumentNullException(nameof(dominance));
    private readonly IControlFlowPostDominanceAnalyzer _postDominance =
        postDominance ?? throw new ArgumentNullException(nameof(postDominance));
    private readonly IControlFlowComponentAnalyzer _components =
        components ?? throw new ArgumentNullException(nameof(components));
    private readonly IControlFlowNaturalLoopAnalyzer _naturalLoops =
        naturalLoops ?? throw new ArgumentNullException(nameof(naturalLoops));

    public ControlFlowGraphAnalysis Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!blocks.Contains(entry))
        {
            throw new ArgumentException("the analysis entry must belong to its block set");
        }
        return new ControlFlowGraphAnalysis(
            entry,
            blocks,
            _dominance.Analyze(graph, entry, blocks).ToImmutableDictionary(),
            _postDominance.Analyze(graph, null, blocks).ToImmutableDictionary(),
            _components.Analyze(graph, blocks),
            _naturalLoops.Analyze(graph, entry, blocks));
    }
}
