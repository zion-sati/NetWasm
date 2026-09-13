using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptResultLengthValidator
{
    void Validate(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        InteropMarshallingTarget target);
}
