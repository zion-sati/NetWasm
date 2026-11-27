// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
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
        var discoveryGraph = _exceptionContinuations.Project(validated.Graph);
        var state = new ControlFlowStructuringState(validated)
        {
            ContinuationGraph = discoveryGraph,
        };
        var domains = new ControlFlowDomains(_domains.Find(discoveryGraph));
        var naturalLoops = domains
            .SelectMany(domain => FindNaturalLoops(discoveryGraph, domain)
                .Select(component => new NaturalLoopCandidate(component, domain)))
            .ToArray();
        // The natural loops holding each block: a cycle lies in a loop only if a loop holding one of its blocks holds
        // all of them, so each cycle is checked against those loops, not every loop of the method.
        var loopsByBlock = new Dictionary<int, List<ImmutableHashSet<int>>>();
        foreach (var candidate in naturalLoops)
        {
            foreach (var block in candidate.Component)
            {
                (CollectionsMarshal.GetValueRefOrAddDefault(loopsByBlock, block, out _) ??= []).Add(candidate.Component);
            }
        }
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
            state.LoopsByHeader.Add(loop.Header, loop);
        }
        foreach (var domain in domains)
        {
            foreach (var component in FindStronglyConnectedComponents(
                         discoveryGraph,
                         domain.Blocks).Where(component =>
                         _cycles.Classify(discoveryGraph, component)))
            {
                if (component.IsEmpty
                        ? naturalLoops.Length == 0
                        : !(loopsByBlock.TryGetValue(component.First(), out var holding) &&
                            holding.Exists(loop => component.All(loop.Contains))))
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
