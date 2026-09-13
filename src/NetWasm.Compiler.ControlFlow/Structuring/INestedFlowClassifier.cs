namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface INestedFlowClassifier
{
    bool Classify(ControlFlowStructuringState state, ExceptionGroupSource source, ExceptionGroupSource[] children);
}
