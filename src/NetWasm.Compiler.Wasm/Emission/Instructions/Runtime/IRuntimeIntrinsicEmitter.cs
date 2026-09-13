using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IRuntimeIntrinsicEmitter
{
    void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code);
}
