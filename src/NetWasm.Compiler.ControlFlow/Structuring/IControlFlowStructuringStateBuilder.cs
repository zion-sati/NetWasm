namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IControlFlowStructuringStateBuilder
{
    ControlFlowStructuringState Build(ValidatedControlFlowGraph validated);
}
