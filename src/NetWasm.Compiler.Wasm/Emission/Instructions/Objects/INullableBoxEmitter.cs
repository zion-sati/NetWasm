using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface INullableBoxEmitter
{
    void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity underlyingType);
}
