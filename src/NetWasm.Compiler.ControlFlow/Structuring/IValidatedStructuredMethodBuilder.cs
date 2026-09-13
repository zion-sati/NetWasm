namespace NetWasm.Compiler.ControlFlow.Structuring;

public interface IValidatedStructuredMethodBuilder
{
    Structured.StructuredMethod Build(ValidatedControlFlowGraph validated);
}
