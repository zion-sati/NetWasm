using Draft = NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Linq;
using Final = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ValidatedStructuredMethodBuilder(
    IControlFlowStructuringStateBuilder stateBuilder,
    IExceptionGroupStructurer exceptionGroups,
    IStructuredControlFlowBuilder structuredControlFlow,
    Draft.IExceptionRegionOwnershipProjectorDraft exceptionRegionOwnership,
    Draft.IStructuredControlFlowOwnershipResolverDraft ownership,
    Draft.IStructuredControlFlowValidatorDraft validator,
    Final.IStructuredMethodCompleter completer) : IValidatedStructuredMethodBuilder
{
    private readonly IControlFlowStructuringStateBuilder _stateBuilder =
        stateBuilder ?? throw new ArgumentNullException(nameof(stateBuilder));
    private readonly IExceptionGroupStructurer _exceptionGroups =
        exceptionGroups ?? throw new ArgumentNullException(nameof(exceptionGroups));
    private readonly IStructuredControlFlowBuilder _structuredControlFlow =
        structuredControlFlow ?? throw new ArgumentNullException(nameof(structuredControlFlow));
    private readonly Draft.IExceptionRegionOwnershipProjectorDraft _exceptionRegionOwnership =
        exceptionRegionOwnership ?? throw new ArgumentNullException(nameof(exceptionRegionOwnership));
    private readonly Draft.IStructuredControlFlowOwnershipResolverDraft _ownership =
        ownership ?? throw new ArgumentNullException(nameof(ownership));
    private readonly Draft.IStructuredControlFlowValidatorDraft _validator =
        validator ?? throw new ArgumentNullException(nameof(validator));
    private readonly Final.IStructuredMethodCompleter _completer =
        completer ?? throw new ArgumentNullException(nameof(completer));

    public Final.StructuredMethod Build(ValidatedControlFlowGraph validated)
    {
        ArgumentNullException.ThrowIfNull(validated);
        var state = _stateBuilder.Build(validated);
        var exceptionGroups = _exceptionGroups.Structure(
            state,
            validated.Graph.MethodBody.ExceptionRegions);
        state.ExceptionGroupsByEntry = exceptionGroups.ToDictionary(
            group => state.Graph.GetBlockAtOffset(group.TryOffset).Index);
        var body = _structuredControlFlow.Build(
            state,
            validated.Graph.Entry.Index,
            stop: null,
            allowed: validated.Graph.ReachableBlocks,
            new HashSet<int>());
        var draft = _ownership.Resolve(_exceptionRegionOwnership.Project(new Draft.StructuredMethodDraft(
            validated,
            body,
            exceptionGroups,
            [])));
        _validator.Validate(draft);
        return _completer.Complete(draft);
    }
}
