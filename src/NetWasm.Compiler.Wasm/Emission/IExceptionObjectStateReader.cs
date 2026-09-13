using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IExceptionObjectStateReader
{
    void Emit(
        IWasmInstructionWriter code,
        int exceptionLocal,
        int typeIdLocal,
        int messageLocal,
        int messageLengthLocal);
}
