using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface INullableUnboxAnyEmitter
{
    void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity nullableType,
        CliTypeIdentity underlyingType);
}
