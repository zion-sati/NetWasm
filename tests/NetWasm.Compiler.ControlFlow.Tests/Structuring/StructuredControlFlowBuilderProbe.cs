using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

internal sealed class StructuredControlFlowBuilderProbe : IStructuredControlFlowBuilder
{
    internal int CallCount { get; private set; }

    internal List<SequenceBuildCall> SequenceCalls { get; } = [];

    internal List<DispatcherBuildCall> DispatcherCalls { get; } = [];

    internal StructuredDispatcherDraft DispatcherResult { get; } = new(
        null,
        [],
        []);

    public StructuredSequenceDraft Build(
        ControlFlowStructuringState state,
        int? start,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path)
    {
        CallCount++;
        SequenceCalls.Add(new(start, stop, allowed, path));
        return new StructuredSequenceDraft(ImmutableArray<StructuredRegionDraft>.Empty);
    }

    public StructuredDispatcherDraft Build(
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? exitStop,
        HashSet<int> path)
    {
        CallCount++;
        DispatcherCalls.Add(new(entry, component, allowed, exitStop, path));
        return DispatcherResult;
    }

    internal sealed record DispatcherBuildCall(
        int? Entry,
        ImmutableHashSet<int> Component,
        ImmutableHashSet<int> Allowed,
        int? ExitStop,
        HashSet<int> Path);

    internal sealed record SequenceBuildCall(
        int? Start,
        int? Stop,
        ImmutableHashSet<int> Allowed,
        HashSet<int> Path);
}
