using System;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class StructuredControlFlowOwnershipResolverDraft :
    IStructuredControlFlowOwnershipResolverDraft
{
    private readonly IStructuredControlFlowOccurrenceCollector _occurrenceCollector;
    private readonly IStructuredControlFlowOwnerSelector _ownerSelector;
    private readonly IStructuredControlFlowOwnershipProjector _ownershipProjector;
    internal StructuredControlFlowOwnershipResolverDraft(
        IStructuredControlFlowOccurrenceCollector occurrenceCollector,
        IStructuredControlFlowOwnerSelector ownerSelector,
        IStructuredControlFlowOwnershipProjector ownershipProjector)
    {
        _occurrenceCollector = occurrenceCollector ??
            throw new ArgumentNullException(nameof(occurrenceCollector));
        _ownerSelector = ownerSelector ??
            throw new ArgumentNullException(nameof(ownerSelector));
        _ownershipProjector = ownershipProjector ??
            throw new ArgumentNullException(nameof(ownershipProjector));
    }

    public StructuredMethodDraft Resolve(StructuredMethodDraft method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var occurrences = _occurrenceCollector.Collect(method.Body);
        var selectedOwners = _ownerSelector.Select(occurrences);
        return _ownershipProjector.Project(method, selectedOwners);
    }
}
