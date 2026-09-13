using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IExceptionPayloadBlockEmitter
{
    void Emit(IWasmInstructionWriter code);
}
