using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptResultHandleCapturer
{
    void Capture(IWasmInstructionWriter code, MethodEmissionContext context);
}
