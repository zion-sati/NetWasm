using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryAlignedAllocIntrinsicEmitter(
    INativeMemoryAllocationEmitter allocations) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        allocations.Emit(request, code, RuntimeImportSymbol.NativeAlignedAlloc, 2);
}
