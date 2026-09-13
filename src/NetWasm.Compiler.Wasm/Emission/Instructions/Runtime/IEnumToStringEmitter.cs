using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumToStringEmitter
{
    void EmitToString(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
