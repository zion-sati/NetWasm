using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopExitPartitioner(
    ILoopExitExtensionCollector exitExtensions,
    IReachableSetOverlapClassifier reachableSetOverlaps) : ILoopExitPartitioner
{
    private readonly ILoopExitExtensionCollector _exitExtensions =
        exitExtensions ?? throw new ArgumentNullException(nameof(exitExtensions));
    private readonly IReachableSetOverlapClassifier _reachableSetOverlaps =
        reachableSetOverlaps ?? throw new ArgumentNullException(nameof(reachableSetOverlaps));

    public LoopExitPartition Partition(
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        int exit,
        int conditionExit,
        ImmutableHashSet<int> additionalExits)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(additionalExits);

        var bodyExitExtension = _exitExtensions.Collect(
            graph,
            component,
            exit,
            additionalExits);
        var conditionExitExtension = _exitExtensions.Collect(
            graph,
            component,
            exit,
            [conditionExit]);
        var bodyComponent = component.Union(bodyExitExtension);

        return new LoopExitPartition(
            conditionExitExtension,
            bodyComponent,
            _reachableSetOverlaps.Overlaps([bodyComponent, conditionExitExtension]));
    }
}
