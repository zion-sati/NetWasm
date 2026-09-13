using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumParseEmitter
{
    void EmitParse(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
