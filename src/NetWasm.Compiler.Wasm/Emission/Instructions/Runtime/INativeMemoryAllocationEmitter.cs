using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface INativeMemoryAllocationEmitter
{
    void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        RuntimeImportSymbol symbol,
        int argumentCount);
}
