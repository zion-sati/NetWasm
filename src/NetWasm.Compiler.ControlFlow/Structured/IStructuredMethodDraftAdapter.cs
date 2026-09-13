using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredMethodDraftAdapter
{
    StructuredMethod Adapt(Draft.StructuredMethodDraft draft);
}
