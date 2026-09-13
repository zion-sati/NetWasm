using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class CilOperandReader
{
    public static int GetIndex(CilInstruction instruction) =>
        ((CilOperand.Index)instruction.Operand).Value;

    public static EntityKey GetEntity(CilInstruction instruction) =>
        ((CilOperand.Entity)instruction.Operand).Key;
}
