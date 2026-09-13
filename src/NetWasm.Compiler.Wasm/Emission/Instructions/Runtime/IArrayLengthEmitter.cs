using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IArrayLengthEmitter
{
    void EmitLength(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
