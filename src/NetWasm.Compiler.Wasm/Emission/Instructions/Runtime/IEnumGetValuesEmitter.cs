using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumGetValuesEmitter
{
    void EmitGetValues(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
