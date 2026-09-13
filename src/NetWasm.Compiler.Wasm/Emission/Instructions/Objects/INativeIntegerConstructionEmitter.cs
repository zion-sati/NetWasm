using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface INativeIntegerConstructionEmitter
{
    void Emit(NativeIntegerConstructionRequest request, IWasmInstructionWriter code);
}
