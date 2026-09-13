namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IStructuredControlFlowOwnershipResolverDraft
{
    StructuredMethodDraft Resolve(StructuredMethodDraft method);
}
