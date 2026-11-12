using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

// How a static field's load reads a constant the field is proven to hold: a primitive or enum
// field reads it as its width stores it, and a reference field reads null. The folds that replace
// such a load with its value share this rule.
internal static class StaticFieldConstantLoads
{
    // The type a field of this signature holds a constant as: the reference type itself, a
    // primitive, or an enum's underlying primitive; null for any other, as a struct or a pointer.
    public static (bool Reference, CliTypeIdentity Type)? Storage(
        CliTypeIdentity signature,
        ITypeDefinitionResolver typeDefinitions)
    {
        if (signature is
            {
                IsValueType: false, Shape: not (CliTypeShape.GenericTypeParameter or
                CliTypeShape.GenericMethodParameter or CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or
                CliTypeShape.FunctionPointer or CliTypeShape.Modified)
            })
        {
            return (true, signature);
        }
        if (signature.Shape == CliTypeShape.Primitive) return (false, signature);
        if (signature.Shape != CliTypeShape.Named || !signature.IsValueType) return null;
        var definition = typeDefinitions.ResolveTypeIdentity(signature);
        return definition.IsEnum && definition.EnumUnderlyingType.Shape == CliTypeShape.Primitive
            ? (false, definition.EnumUnderlyingType)
            : null;
    }

    // The evaluation stack kind a constant of this storage is held as.
    public static CliValueKind StackKind(bool reference, CliTypeIdentity type) =>
        reference ? CliValueKind.ManagedReference : type.StackKind is CliValueKind.F4 ? CliValueKind.F8 : type.StackKind;

    // The load of a constant as the stack holds it, or null where this storage cannot hold it.
    public static CilInstruction? Load(bool reference, CliTypeIdentity type, CliValueKind kind, long integer, double real) =>
        (reference, type.CanonicalName, kind) switch
        {
            (true, _, CliValueKind.ManagedReference) => Instruction(CilOperation.LoadNull, new CilOperand.None()),
            (false, "primitive:bool" or "primitive:u1", CliValueKind.I4) => Int32((byte)integer),
            (false, "primitive:i1", CliValueKind.I4) => Int32((sbyte)integer),
            (false, "primitive:char" or "primitive:u2", CliValueKind.I4) => Int32((ushort)integer),
            (false, "primitive:i2", CliValueKind.I4) => Int32((short)integer),
            (false, "primitive:i4" or "primitive:u4", CliValueKind.I4) => Int32((int)integer),
            (false, "primitive:i8" or "primitive:u8", CliValueKind.I8) =>
                Instruction(CilOperation.LoadInt64, new CilOperand.ConstantI8(integer)),
            (false, "primitive:f4", CliValueKind.F8) => Instruction(CilOperation.LoadFloat32, new CilOperand.ConstantF4((float)real)),
            (false, "primitive:f8", CliValueKind.F8) => Instruction(CilOperation.LoadFloat64, new CilOperand.ConstantF8(real)),
            _ => null,
        };

    public static CilInstruction Instruction(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);

    private static CilInstruction Int32(int value) => Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
}
