using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumNumericFormatter
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity underlying,
        int value,
        int payload,
        int? format,
        int result,
        int raw,
        int scratch);
}
