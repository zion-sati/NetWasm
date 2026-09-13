namespace NetWasm.Compiler.ControlFlow.Structured;

public interface IStructuredInstructionContractValidator
{
    void Validate(StructuredMethod method);
}
