namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredBlockOwnershipValidator
{
    void Validate(StructuredMethod method);
}
