using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface IEnumTypeArgumentValidator
{
    void Validate(IWasmInstructionWriter code, int typeLocal, int typeIdLocal);
}
