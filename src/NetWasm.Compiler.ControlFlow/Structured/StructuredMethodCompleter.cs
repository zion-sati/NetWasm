using System;
using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredMethodCompleter(
    IStructuredMethodDraftAdapter drafts,
    IStructuredMethodValidator validator) : IStructuredMethodCompleter
{
    private readonly IStructuredMethodDraftAdapter _drafts =
        drafts ?? throw new ArgumentNullException(nameof(drafts));
    private readonly IStructuredMethodValidator _validator =
        validator ?? throw new ArgumentNullException(nameof(validator));

    public StructuredMethod Complete(Draft.StructuredMethodDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var method = _drafts.Adapt(draft);
        _validator.Validate(method);
        return method;
    }
}
