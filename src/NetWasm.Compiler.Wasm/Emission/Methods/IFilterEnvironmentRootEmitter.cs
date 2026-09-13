using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IFilterEnvironmentRootEmitter
{
    void Emit(IWasmInstructionWriter code, MethodEmissionContext context);
}
