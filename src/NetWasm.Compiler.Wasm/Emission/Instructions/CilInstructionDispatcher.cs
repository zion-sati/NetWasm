using System;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed class CilInstructionDispatcher(
    IInstructionCommandResolver commands,
    IRootPublicationEmitter roots,
    IStackStorageAnchorEmitter anchors) : ICilInstructionDispatcher
{
    public void Emit(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = commands.Resolve(request.Instruction.Operation);
        if (!command.PublishesRoots) roots.Emit(request, code);
        command.Emit(request, code, functionIndices);
        anchors.Emit(request, code);
    }
}
