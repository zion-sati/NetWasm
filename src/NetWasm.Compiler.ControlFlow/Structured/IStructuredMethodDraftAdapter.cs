using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredMethodDraftAdapter
{
    StructuredMethodConstruction Adapt(Draft.StructuredMethodDraft draft);
}
