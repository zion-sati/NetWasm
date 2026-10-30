namespace NetWasm.Compiler.Core;

public static class CilTypeMaterialization
{
    // A materialization that names its type, as typeof does, uses the type's
    // static Type object: it neither allocates nor throws.
    public static bool IsConstant(CilInstruction instruction) =>
        instruction is
        {
            Operation: CilOperation.MaterializeType,
            Operand: CilOperand.TypeIdentity or CilOperand.Entity,
        };

    // GetType on a receiver of one possible type checks the receiver for null
    // and uses that type's static Type object.
    public static bool IsExactObjectType(CilInstruction instruction) =>
        instruction is { Operation: CilOperation.GetObjectType, Operand: CilOperand.ExactType };

    // The instructions whose type needs a static Type object.
    public static bool UsesStaticTypeObject(CilInstruction instruction) =>
        IsConstant(instruction) || IsExactObjectType(instruction);
}
