// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A loop left only from a switch's cases (an interpreter's while (true) switch, a state machine that returns from one
// of its states) has no condition of its own: the loop factory would test one of the chain's compares as its
// condition, or make the loop a dispatcher, and either keeps the chain. When the policy tables the switch, the loop is
// unconditional instead, with the whole chain in its body and a case's target as its exit, so the switch step takes
// the chain. Every other loop is the inner factory's.
internal sealed class SwitchLoopRegionFactory(
    ILoopRegionFactory loops,
    ISwitchChainReader chains,
    ISwitchTablePolicy tables,
    ICommonReachableBlockFinder joins,
    ILoopExitPartitioner exitPartitions,
    ILoopRegionEligibilityClassifier eligibility,
    ILoopEntryTargetFinder entryTargets) : ILoopRegionFactory
{
    private readonly ILoopRegionFactory _loops = loops ?? throw new ArgumentNullException(nameof(loops));
    private readonly ISwitchChainReader _chains = chains ?? throw new ArgumentNullException(nameof(chains));
    private readonly ISwitchTablePolicy _tables = tables ?? throw new ArgumentNullException(nameof(tables));
    private readonly ICommonReachableBlockFinder _joins = joins ?? throw new ArgumentNullException(nameof(joins));
    private readonly ILoopExitPartitioner _exitPartitions =
        exitPartitions ?? throw new ArgumentNullException(nameof(exitPartitions));
    private readonly ILoopRegionEligibilityClassifier _eligibility =
        eligibility ?? throw new ArgumentNullException(nameof(eligibility));
    private readonly ILoopEntryTargetFinder _entryTargets =
        entryTargets ?? throw new ArgumentNullException(nameof(entryTargets));

    public LoopRegionCreation Create(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain,
        ControlFlowDomains domains) =>
        Region(state, graph, component, domain, domains) is { } region
            ? new LoopRegionCreation(region, [])
            : _loops.Create(state, graph, component, domain, domains);

    private LoopRegion? Region(ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain,
        ControlFlowDomains domains)
    {
        // A loop that enters a protected region leaves through its continuations: the loop factory's.
        if (LoopRegionFactory.Header(_entryTargets.Find(graph, component, domain, domains), component, domain) is not int header ||
            (graph.ProtectedEntries.Count < component.Count
                ? graph.ProtectedEntries.Any(component.Contains)
                : component.Any(graph.ProtectedEntries.Contains)))
        {
            return null;
        }
        // The blocks the loop is left from, all of them a switch's head or compares.
        var sources = component.Where(block => graph.Successors[block].Any(successor => !component.Contains(successor))).ToArray();
        var chain = component.Order()
            .Select(block => _chains.Read(state.Validated, graph.GetBlock(block)))
            .FirstOrDefault(chain => chain is not null && sources.Length != 0 &&
                sources.All(source => source == chain.Head.Index || chain.Blocks.Any(block => block.Index == source)));
        if (chain is null || !_tables.Tables(chain.Cases.Length, chain.Targets.Length))
        {
            return null;
        }
        var body = component.Union(chain.Blocks.Select(block => block.Index));
        var exits = body.SelectMany(block => graph.Successors[block]).Where(successor => !body.Contains(successor))
            .ToImmutableHashSet();
        var exit = _joins.Find(state, [.. exits.Order()], stop: null, domain.Blocks) ?? exits.Min();
        var partition = _exitPartitions.Partition(state.ContinuationGraph, body, exit, exit, exits);
        if (partition.Overlaps)
        {
            return null;
        }
        var region = new LoopRegion(header, null, header, exit, exit, component, partition.BodyComponent, []);
        return _eligibility.Classify(new(graph, component, region, domain, domains)) ? region : null;
    }
}
