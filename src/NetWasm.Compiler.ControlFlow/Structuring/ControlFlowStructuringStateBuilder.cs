using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowStructuringStateBuilder(
    IExceptionContinuationGraphProjector exceptionContinuations,
    IControlFlowComponentAnalyzer components,
    IControlFlowNaturalLoopAnalyzer naturalLoops,
    IControlFlowDomainFinder domains,
    ILoopRegionFactory loops,
    IControlFlowCycleClassifier cycles) : IControlFlowStructuringStateBuilder
{
    private readonly IExceptionContinuationGraphProjector _exceptionContinuations = exceptionContinuations ?? throw new ArgumentNullException(nameof(exceptionContinuations));
    private readonly IControlFlowComponentAnalyzer _components = components ?? throw new ArgumentNullException(nameof(components));
    private readonly IControlFlowNaturalLoopAnalyzer _naturalLoops = naturalLoops ?? throw new ArgumentNullException(nameof(naturalLoops));
    private readonly IControlFlowDomainFinder _domains = domains ?? throw new ArgumentNullException(nameof(domains));
    private readonly ILoopRegionFactory _loops = loops ?? throw new ArgumentNullException(nameof(loops));
    private readonly IControlFlowCycleClassifier _cycles = cycles ?? throw new ArgumentNullException(nameof(cycles));

    public ControlFlowStructuringState Build(ValidatedControlFlowGraph validated)
    {
        ArgumentNullException.ThrowIfNull(validated);
        var state = new ControlFlowStructuringState(validated);
        var discoveryGraph = _exceptionContinuations.Project(validated.Graph);
        var domains = _domains.Find(discoveryGraph);
        var naturalLoops = domains
            .SelectMany(domain => FindNaturalLoops(discoveryGraph, domain)
                .Select(component => new NaturalLoopCandidate(component, domain)))
            .ToArray();
        foreach (var candidate in naturalLoops)
        {
            var loopCreation = _loops.Create(state,
                validated.Graph,
                candidate.Component,
                candidate.Domain,
                domains);
            var loop = loopCreation.Region;
            if (loop is null)
            {
                foreach (var node in loopCreation.DispatcherOwnedBlocks)
                {
                    state.DispatchersByNode.TryAdd(node, loopCreation.DispatcherOwnedBlocks);
                }
                continue;
            }
            if (loop.Component.SetEquals(candidate.Component) ||
                !validated.Graph.MethodBody.ExceptionRegions.IsEmpty)
            {
                state.LoopsByHeader.Add(loop.Header, loop);
            }
            else
            {
                foreach (var node in candidate.Component)
                {
                    state.DispatchersByNode.TryAdd(node, candidate.Component);
                }
            }
        }
        foreach (var domain in domains)
        {
            foreach (var component in FindStronglyConnectedComponents(
                         discoveryGraph,
                         domain.Blocks).Where(component =>
                         _cycles.Classify(discoveryGraph, component)))
            {
                if (!naturalLoops.Any(loop =>
                        component.IsSubsetOf(loop.Component)))
                {
                    foreach (var node in component)
                    {
                        state.DispatchersByNode.TryAdd(node, component);
                    }
                }
            }
        }

        return state;
    }

    private ImmutableArray<ImmutableHashSet<int>> FindStronglyConnectedComponents(
        ControlFlowGraph graph,
        ImmutableHashSet<int> allowed) =>
        _components.Analyze(graph, allowed);

    private ImmutableArray<ImmutableHashSet<int>> FindNaturalLoops(
        ControlFlowGraph graph,
        ControlFlowDomain domain) =>
        _naturalLoops.Analyze(
            graph,
            domain.Entry,
            domain.Blocks);
}
