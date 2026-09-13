using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests;

using static ControlFlowTestSupport;

public sealed class ControlFlowGraphAnalysisTests
{
    [Fact]
    public void ReportsDominatorsPostDominatorsCyclesAndNaturalLoops()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
            I(3, CilOperation.Nop),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(5, CilOperation.Return)));

        var analysis = CreateGraphAnalyzer().Analyze(
            graph,
            graph.Entry.Index,
            graph.ReachableBlocks);

        Assert.Contains(1, analysis.Dominators[2]);
        Assert.Contains(3, analysis.PostDominators[0]);
        Assert.Contains(
            analysis.StronglyConnectedComponents,
            component => component.SetEquals([1, 2]));
        Assert.Contains(
            analysis.NaturalLoops,
            loop => loop.SetEquals([1, 2]));

        Assert.Equal(graph.ReachableBlocks, analysis.Blocks);
        Assert.Equal(analysis.Entry, graph.Entry.Index);
    }

    [Fact]
    public void RejectsAnEntryOutsideTheRequestedDomain()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Return)));

        var exception = Assert.Throws<ArgumentException>(() =>
            CreateGraphAnalyzer().Analyze(graph, 1, graph.ReachableBlocks));

        Assert.Contains("entry", exception.Message);
    }

    [Fact]
    public void AnalyzersRejectMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ControlFlowNaturalLoopAnalyzer(null!));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzer(
            null!,
            CreatePostDominanceAnalyzer(),
            CreateComponentAnalyzer(),
            CreateNaturalLoopAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzer(
            CreateDominanceAnalyzer(),
            null!,
            CreateComponentAnalyzer(),
            CreateNaturalLoopAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzer(
            CreateDominanceAnalyzer(),
            CreatePostDominanceAnalyzer(),
            null!,
            CreateNaturalLoopAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzer(
            CreateDominanceAnalyzer(),
            CreatePostDominanceAnalyzer(),
            CreateComponentAnalyzer(),
            null!));
    }

    [Fact]
    public void AnalysisFactoryRejectsMissingCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzerFactory(
            null!,
            CreatePostDominanceAnalyzer(),
            CreateComponentAnalyzer(),
            CreateNaturalLoopAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzerFactory(
            CreateDominanceAnalyzer(),
            null!,
            CreateComponentAnalyzer(),
            CreateNaturalLoopAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzerFactory(
            CreateDominanceAnalyzer(),
            CreatePostDominanceAnalyzer(),
            null!,
            CreateNaturalLoopAnalyzer()));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowGraphAnalyzerFactory(
            CreateDominanceAnalyzer(),
            CreatePostDominanceAnalyzer(),
            CreateComponentAnalyzer(),
            null!));
    }

    [Fact]
    public void DefaultAnalysisFactoryComposesItsAnalyzerContract()
    {
        var factory = new ControlFlowGraphAnalyzerFactory();
        Assert.IsAssignableFrom<IControlFlowGraphAnalyzer>(factory.Create());
    }

    [Fact]
    public void IndividualAnalysisContractsProduceTheirSpecificResults()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
            I(3, CilOperation.Nop),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(5, CilOperation.Return)));

        var dominance = CreateDominanceAnalyzer().Analyze(
            graph,
            graph.Entry.Index,
            graph.ReachableBlocks);
        var postDominance = CreatePostDominanceAnalyzer().Analyze(
            graph,
            null,
            graph.ReachableBlocks);
        var components = CreateComponentAnalyzer().Analyze(
            graph,
            graph.ReachableBlocks);
        var loops = CreateNaturalLoopAnalyzer().Analyze(
            graph,
            graph.Entry.Index,
            graph.ReachableBlocks);

        Assert.Contains(1, dominance[2]);
        Assert.Contains(3, postDominance[0]);
        Assert.Contains(components, component => component.SetEquals([1, 2]));
        Assert.Contains(loops, loop => loop.SetEquals([1, 2]));
    }

    [Fact]
    public void DominanceContractSeedsARequestedDisconnectedBlockWithoutPredecessors()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Return),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Return)));
        var blocks = graph.Blocks.Select(block => block.Index).ToImmutableHashSet();

        var result = CreateDominanceAnalyzer().Analyze(graph, graph.Entry.Index, blocks);

        var disconnected = graph.Blocks.Single(block => block.StartOffset == 1).Index;
        Assert.Equal([disconnected], result[disconnected]);
    }

    [Fact]
    public void ReachabilityStopsAtEveryRequestedBoundaryAndRejectsDisallowedEntries()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
            I(3, CilOperation.Nop),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(5, CilOperation.Return)));
        var stop = graph.GetBlockAtOffset(5).Index;
        var finder = new ReachableBlockFinder(new ControlFlowDistanceFinder());

        var reachable = finder.Find(
            graph,
            graph.Entry.Index,
            ImmutableHashSet.Create(stop),
            graph.ReachableBlocks);
        var disallowed = finder.Find(
            graph,
            graph.Entry.Index,
            ImmutableHashSet<int>.Empty,
            ImmutableHashSet<int>.Empty);

        Assert.DoesNotContain(stop, reachable);
        Assert.Contains(graph.Entry.Index, reachable);
        Assert.Empty(disallowed);
    }
}
