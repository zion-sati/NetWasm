using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IUnboxEmitter
{
    void Unbox(InstructionEmissionRequest request, IWasmInstructionWriter code, bool copyValue);

    void Unbox(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        bool copyValue);
}
