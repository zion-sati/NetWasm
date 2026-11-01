using System;

namespace NetWasm.Compiler.Core;

// What a field access, call or typed instruction names. The decoder writes a
// field or method as its definition's key, or as an instance where it is
// constructed or its signature is not its definition's, and a type as its
// identity; rewrites write instances and identities.
public static class CilOperands
{
    public static EntityKey FieldKey(CilOperand operand) => operand switch
    {
        CilOperand.FieldInstance instance => instance.Value.Definition.Key,
        CilOperand.Entity entity => entity.Key,
        _ => throw new InvalidOperationException($"Operand '{operand}' names no field."),
    };

    public static EntityKey MethodKey(CilOperand operand) => operand switch
    {
        CilOperand.MethodInstance instance => instance.Value.Definition.Key,
        CilOperand.Entity entity => entity.Key,
        _ => throw new InvalidOperationException($"Operand '{operand}' names no method."),
    };

    public static CliTypeIdentity Type(CilOperand operand) =>
        operand is CilOperand.TypeIdentity { Value: var type }
            ? type
            : throw new InvalidOperationException($"Operand '{operand}' names no type.");
}
