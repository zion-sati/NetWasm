using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IMethodFrameExitEmitter
{
    void Emit(IWasmInstructionWriter code, MethodEmissionContext context);
}
