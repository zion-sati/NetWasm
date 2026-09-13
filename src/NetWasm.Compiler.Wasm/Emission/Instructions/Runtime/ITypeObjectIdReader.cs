using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal interface ITypeObjectIdReader
{
    void Read(IWasmInstructionWriter code, int typeLocal, int typeIdLocal);
}
