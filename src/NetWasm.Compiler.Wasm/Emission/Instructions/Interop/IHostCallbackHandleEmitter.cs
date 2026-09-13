using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IHostCallbackHandleEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        int delegateLocal,
        MethodEmissionContext context);
}
