using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Support;

internal interface IAddressInstructionEmitter
{
    void Emit(IWasmInstructionWriter code, int constant);

    void Emit(IWasmInstructionWriter code, AddressOperation operation);
}
