using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IArrayReceiverValidator
{
    void Validate(IWasmInstructionWriter code, int receiverLocal);
}
