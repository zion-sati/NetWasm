using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumIsDefinedEmitter
{
    void EmitIsDefined(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
