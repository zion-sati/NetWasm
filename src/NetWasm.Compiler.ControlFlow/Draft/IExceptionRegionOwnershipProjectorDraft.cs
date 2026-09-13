namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IExceptionRegionOwnershipProjectorDraft
{
    StructuredMethodDraft Project(StructuredMethodDraft method);
}
