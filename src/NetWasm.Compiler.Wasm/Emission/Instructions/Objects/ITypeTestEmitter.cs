using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface ITypeTestEmitter
{
    void Test(InstructionEmissionRequest request, IWasmInstructionWriter code, bool returnNullOnFailure);
}
