using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IInstructionCommandResolver
{
    IInstructionCommand Resolve(CilOperation operation);
}
