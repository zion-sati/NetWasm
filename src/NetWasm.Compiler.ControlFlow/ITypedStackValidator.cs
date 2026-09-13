namespace NetWasm.Compiler.ControlFlow;

public interface ITypedStackValidator
{
    ValidatedControlFlowGraph Validate(ControlFlowGraph graph);
}
