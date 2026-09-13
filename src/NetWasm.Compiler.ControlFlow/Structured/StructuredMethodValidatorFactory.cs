namespace NetWasm.Compiler.ControlFlow.Structured;

public sealed class StructuredMethodValidatorFactory : IStructuredMethodValidatorFactory
{
    public IStructuredMethodValidator Create() => new StructuredMethodValidator(
        new StructuredInstructionContractValidator(),
        new StructuredBlockOwnershipValidator(),
        new StructuredTargetValidator(),
        new StructuredExceptionValidator(),
        new StructuredStackContractValidator());
}
