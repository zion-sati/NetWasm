using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICheckedBinaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind type,
        int leftLocal,
        int rightLocal,
        int resultLocal);
}
