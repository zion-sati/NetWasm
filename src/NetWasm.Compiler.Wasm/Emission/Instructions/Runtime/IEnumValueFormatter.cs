using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumValueFormatter
{
    void Emit(
        IWasmInstructionWriter code,
        EnumMetadataLayout metadata,
        CliTypeIdentity underlyingType,
        int valueLocal,
        int payloadOffset,
        int? formatLocal,
        int resultLocal,
        int rawLocal,
        int scratchLocal);
}
