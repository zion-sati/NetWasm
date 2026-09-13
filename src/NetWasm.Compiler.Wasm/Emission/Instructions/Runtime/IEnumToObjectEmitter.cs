using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumToObjectEmitter
{
    void EmitToObject(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
