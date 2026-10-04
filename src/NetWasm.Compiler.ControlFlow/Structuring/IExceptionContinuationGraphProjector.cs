namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IExceptionContinuationGraphProjector
{
    ControlFlowGraph Project(ControlFlowGraph graph);
}
