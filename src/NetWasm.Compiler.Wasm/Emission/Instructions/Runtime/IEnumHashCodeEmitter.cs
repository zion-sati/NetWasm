using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumHashCodeEmitter
{
    void EmitHashCode(
        IWasmInstructionWriter code,
        CliValueKind receiverKind,
        CliTypeIdentity? receiverType,
        int receiver,
        int result,
        int temporaryI4,
        int temporaryI8);
}
