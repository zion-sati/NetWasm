using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumGetNameEmitter
{
    void EmitGetName(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
