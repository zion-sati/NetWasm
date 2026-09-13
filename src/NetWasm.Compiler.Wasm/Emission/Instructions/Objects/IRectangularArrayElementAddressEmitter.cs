using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IRectangularArrayElementAddressEmitter
{
    void Emit(RectangularArrayElementAddressRequest request, IWasmInstructionWriter code);
}
