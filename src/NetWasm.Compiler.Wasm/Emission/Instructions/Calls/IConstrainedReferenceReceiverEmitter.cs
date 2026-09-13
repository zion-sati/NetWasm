using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface IConstrainedReferenceReceiverEmitter
{
    int? Emit(
        InstructionEmissionRequest instruction,
        IWasmInstructionWriter code,
        int argumentBase,
        CliTypeIdentity? constrainedType);
}
