using NetWasm.Compiler.Wasm.Emission.Instructions;

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IRootPublicationEmitter
{
    void Emit(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        bool constructorCall = false);
}
