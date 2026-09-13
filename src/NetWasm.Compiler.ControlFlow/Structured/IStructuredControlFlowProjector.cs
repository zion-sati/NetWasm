using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draft = NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredControlFlowProjector
{
    StructuredSequence Project(
        Draft.StructuredSequenceDraft sequence,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup);

    StructuredExceptionPart Project(
        Draft.StructuredExceptionPartDraft part,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft activeGroup);

    StructuredDispatcher Project(
        Draft.StructuredDispatcherDraft dispatcher,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup);
}
