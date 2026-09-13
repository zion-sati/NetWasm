using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal interface IFunctionLoader
{
    void Load(InstructionEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices);
}
