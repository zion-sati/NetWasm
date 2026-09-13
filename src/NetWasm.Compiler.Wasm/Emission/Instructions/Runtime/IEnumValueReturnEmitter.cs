using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumValueReturnEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        RuntimeIntrinsicEmissionRequest request,
        int valueLocal);
}
