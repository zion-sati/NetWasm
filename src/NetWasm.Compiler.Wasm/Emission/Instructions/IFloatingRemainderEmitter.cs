using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IFloatingRemainderEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliValueKind type,
        int leftLocal,
        int rightLocal,
        int countLocal,
        int signLocal);
}
