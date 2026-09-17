using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draft = NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredMethodDraftAdapter(
    IStructuredBlockDefinitionFactory blocks,
    global::NetWasm.Compiler.ControlFlow.Draft.IExceptionGroupCollector groupCollector,
    IExceptionGroupIdAssigner groupIdAssigner,
    global::NetWasm.Compiler.ControlFlow.Draft.IExceptionGroupParentMapBuilder parentMapBuilder,
    IStructuredControlFlowProjector controlFlowProjector,
    IStructuredExceptionGroupProjector exceptionGroupProjector) : IStructuredMethodDraftAdapter
{
    private readonly IStructuredBlockDefinitionFactory _blocks =
        blocks ?? throw new ArgumentNullException(nameof(blocks));
    private readonly global::NetWasm.Compiler.ControlFlow.Draft.IExceptionGroupCollector _groups =
        groupCollector ?? throw new ArgumentNullException(nameof(groupCollector));
    private readonly IExceptionGroupIdAssigner _groupIds =
        groupIdAssigner ?? throw new ArgumentNullException(nameof(groupIdAssigner));
    private readonly global::NetWasm.Compiler.ControlFlow.Draft.IExceptionGroupParentMapBuilder _parents =
        parentMapBuilder ?? throw new ArgumentNullException(nameof(parentMapBuilder));
    private readonly IStructuredControlFlowProjector _controlFlow =
        controlFlowProjector ?? throw new ArgumentNullException(nameof(controlFlowProjector));
    private readonly IStructuredExceptionGroupProjector _exceptionGroups =
        exceptionGroupProjector ?? throw new ArgumentNullException(nameof(exceptionGroupProjector));

    public StructuredMethodConstruction Adapt(Draft.StructuredMethodDraft method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var definitions = _blocks.Create(method.ValidatedGraph);
        var groupDrafts = _groups.Collect(method);
        var groupIds = _groupIds.Assign(groupDrafts);
        var groupParents = _parents.Build(groupDrafts);
        var groups = groupIds.ToImmutableDictionary(
            pair => pair.Value,
            pair => _exceptionGroups.Project(
                pair.Key,
                pair.Value,
                (groupParents.TryGetValue(pair.Key, out var parentGroupCandidate)
                    ? parentGroupCandidate
                    : null),
                method,
                definitions,
            groupIds));
        var body = _controlFlow.Project(method.Body, method, definitions, groupIds, activeGroup: null);
        var source = method.ValidatedGraph.Graph.MethodBody;
        return new(
            new(
                source.Method,
                source.MethodInstance,
                source.MaxStack,
                source.Locals,
                source.LocalSignatureTypes,
                source.Instructions),
            new(method.ValidatedGraph.Graph.Entry.Index),
            definitions,
            body,
            method.ExceptionGroups.Select(group => groupIds[group]).ToImmutableArray(),
            groups,
            method.ValidatedGraph.InstructionEntryStacks);
    }
}
