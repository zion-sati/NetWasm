using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumValuePairEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        EnumStorage storage,
        int left,
        int right);
}
