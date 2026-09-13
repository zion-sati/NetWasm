using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface INativeMemoryReleaseEmitter
{
    void Emit(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        RuntimeImportSymbol symbol);
}
