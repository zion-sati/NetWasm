using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredMethodCompleter
{
    StructuredMethod Complete(Draft.StructuredMethodDraft draft);
}
