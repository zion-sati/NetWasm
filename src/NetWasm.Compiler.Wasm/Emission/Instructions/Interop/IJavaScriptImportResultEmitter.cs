using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptImportResultEmitter
{
    void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code);
}
