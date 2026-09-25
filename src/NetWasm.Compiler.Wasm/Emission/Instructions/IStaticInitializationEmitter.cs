using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IStaticInitializationEmitter
{
    void Emit(StaticInitializationEmissionRequest request,
        IWasmInstructionWriter code, IFunctionIndexResolver functionIndices);
}
