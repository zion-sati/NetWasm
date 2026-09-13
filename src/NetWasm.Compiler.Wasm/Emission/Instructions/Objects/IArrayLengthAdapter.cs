using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IArrayLengthAdapter
{
    int Adapt(
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        int stackSlot);
}
