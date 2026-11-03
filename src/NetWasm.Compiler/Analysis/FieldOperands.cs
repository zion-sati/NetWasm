using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// A field operand's identity, its declaring type and its type: a generic
// type's instantiations name distinct fields of one definition, which a write
// to any of them may change.
internal readonly record struct FieldOperand(string Field, EntityKey Definition, string DeclaringType, CliTypeIdentity Type)
{
    // Null for an operand that names no field, for a modified field, as a
    // volatile one, and for a field of an explicit layout, which other fields
    // may overlap: their reads and writes keep their memory semantics.
    public static FieldOperand? Describe(CilOperand operand, IFieldRepository fields)
    {
        var (described, overlappable) = operand switch
        {
            CilOperand.FieldInstance { Value: var field } =>
                (new FieldOperand(field.CanonicalName, field.Definition.Key, field.DeclaringType.CanonicalName, field.FieldType),
                    field.Definition.ExplicitOffset is not null),
            CilOperand.Entity { Key: var key } when fields.GetField(key) is var field =>
                (new FieldOperand(key.ToString(), key, field.DeclaringType.ToString(), field.SignatureType),
                    field.ExplicitOffset is not null),
            _ => (default, false),
        };
        return described.Field is null || overlappable || described.Type.Shape == CliTypeShape.Modified
            ? null
            : described;
    }
}
