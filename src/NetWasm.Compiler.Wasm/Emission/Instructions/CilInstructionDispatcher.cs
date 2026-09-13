using System;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class CilInstructionDispatcher(
    IInstructionCommandResolver commands,
    IRootPublicationEmitter roots) : ICilInstructionDispatcher
{
    public void Emit(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        roots.Emit(request, code);
        commands.Resolve(request.Instruction.Operation).Emit(request, code, functionIndices);
    }
}
