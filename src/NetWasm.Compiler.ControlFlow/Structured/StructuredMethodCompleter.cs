using System;
using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredMethodCompleter(
    IStructuredMethodDraftAdapter drafts,
    IStructuredMethodFactory methods) : IStructuredMethodCompleter
{
    private readonly IStructuredMethodDraftAdapter _drafts =
        drafts ?? throw new ArgumentNullException(nameof(drafts));
    private readonly IStructuredMethodFactory _structuredMethodFactory =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public StructuredMethod Complete(Draft.StructuredMethodDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return _structuredMethodFactory.Create(_drafts.Adapt(draft));
    }
}
