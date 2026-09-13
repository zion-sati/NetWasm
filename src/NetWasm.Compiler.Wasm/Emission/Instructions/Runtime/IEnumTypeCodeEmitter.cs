using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumTypeCodeEmitter
{
    void EmitTypeCode(
        IWasmInstructionWriter code,
        int receiver,
        int result,
        int temporaryI4,
        CliTypeIdentity? constrainedType = null,
        CliValueKind receiverKind = CliValueKind.ManagedReference);
}
