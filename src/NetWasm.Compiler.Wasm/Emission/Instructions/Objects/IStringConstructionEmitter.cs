using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IStringConstructionEmitter
{
    void Emit(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        MethodSignatureModel signature,
        int argumentBase);
}
