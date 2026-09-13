using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumHasFlagEmitter
{
    void EmitHasFlag(
        IWasmInstructionWriter code,
        int receiver,
        int flag,
        int result,
        int temporaryI4,
        CliValueKind receiverKind = CliValueKind.ManagedReference,
        CliTypeIdentity? constrainedType = null);
}
