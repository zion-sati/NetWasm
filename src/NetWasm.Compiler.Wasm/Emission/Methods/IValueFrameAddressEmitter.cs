using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IValueFrameAddressEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        int offset);

    void Emit(
        IWasmInstructionWriter code,
        MethodEmissionContext context,
        FilterCapture capture);
}
