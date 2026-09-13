using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumConvertEmitter
{
    void EmitConvert(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
