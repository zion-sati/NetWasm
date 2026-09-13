using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IHostCallbackStringArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target);
}

internal interface IHostCallbackByteArrayArgumentMarshaller
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity arrayType,
        int handleParameter,
        int destination,
        int temporaryI4,
        int objectTemporary,
        InteropMarshallingTarget target);
}
