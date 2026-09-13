using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IInteropHandleReleaser
{
    void Release(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        InteropMarshallingTarget target);

    void Release(IWasmInstructionWriter code, MethodEmissionContext context);
}
