using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IIrreducibleControlFlowExceptionFactory
{
    CompilerException Create(ControlFlowStructuringState state, string message);
}
