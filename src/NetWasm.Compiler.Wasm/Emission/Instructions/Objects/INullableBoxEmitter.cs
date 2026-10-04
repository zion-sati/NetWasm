using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface INullableBoxEmitter
{
    void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity underlyingType);

    // The caller keeps source storage alive across allocation. Source and target
    // must be distinct address-width locals; the source local is preserved.
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity underlyingType,
        int sourceAddressLocal,
        int targetLocal);
}
