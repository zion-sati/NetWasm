using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumValueBoxEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        EnumMetadataLayout entry,
        int valueLocal,
        int resultLocal);
}
