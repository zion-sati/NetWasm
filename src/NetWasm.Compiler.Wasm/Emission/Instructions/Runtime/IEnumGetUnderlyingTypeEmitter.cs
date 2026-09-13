using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumGetUnderlyingTypeEmitter
{
    void EmitGetUnderlyingType(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
