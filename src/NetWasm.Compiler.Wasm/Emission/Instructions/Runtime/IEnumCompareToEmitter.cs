using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumCompareToEmitter
{
    void EmitCompareTo(
        IWasmInstructionWriter code,
        CliValueKind leftKind,
        CliTypeIdentity? constrainedType,
        int left,
        int right,
        int result,
        int temporaryReference,
        int temporaryI4);
}
