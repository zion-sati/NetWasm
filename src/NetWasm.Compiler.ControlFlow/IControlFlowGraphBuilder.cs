using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public interface IControlFlowGraphBuilder
{
    ControlFlowGraph Build(CilMethodBody methodBody);
}
