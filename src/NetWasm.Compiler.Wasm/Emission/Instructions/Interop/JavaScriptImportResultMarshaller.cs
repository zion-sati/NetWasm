using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptImportResultMarshaller(
    IJavaScriptImportResultKindResolver kinds,
    IJavaScriptImportResultEmitterRegistry emitters) :
    IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code) =>
        emitters.Get(kinds.Resolve(request.Signature.ReturnSignatureType)).Emit(request, code);
}
