using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryAlignedFreeIntrinsicEmitter(
    INativeMemoryReleaseEmitter releases) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        releases.Emit(request, code, RuntimeImportSymbol.NativeAlignedFree);
}
