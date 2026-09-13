using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumGetNamesEmitter
{
    void EmitGetNames(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
