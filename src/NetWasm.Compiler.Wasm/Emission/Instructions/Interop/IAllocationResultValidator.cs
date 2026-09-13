using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IAllocationResultValidator
{
    void Validate(IWasmInstructionWriter code, int objectLocal);
}
