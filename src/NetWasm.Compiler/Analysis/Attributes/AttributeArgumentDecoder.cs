using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeArgumentDecoder(CliTypeIdentity systemType) : IAttributeArgumentDecoder
{
    public AttributeArgumentPlan Decode(CustomAttributeTypedArgument<CliTypeIdentity> argument,
        CliTypeIdentity destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var serialized = argument.Type;
        if (!destination.Equals(serialized) && destination.CanonicalName != "primitive:object")
        {
            throw Unsupported("serialized custom attribute argument does not match its destination type");
        }
        if (argument.Value is null)
        {
            if (serialized.IsValueType) throw Unsupported("a value-type attribute argument cannot be null");
            return Result(AttributeArgumentKind.Null);
        }
        if (argument.Value is ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>> elements)
        {
            if (serialized.Shape != CliTypeShape.SzArray)
                throw Unsupported("custom attribute array payload does not have an array type");
            return elements.IsDefault ? Result(AttributeArgumentKind.Null) :
                Result(AttributeArgumentKind.Array) with
                {
                    Elements = [.. elements.Select(element => Decode(element, serialized.ElementType!))],
                };
        }
        if (serialized.Equals(systemType) && argument.Value is CliTypeIdentity identity)
        {
            return Result(AttributeArgumentKind.Type) with { TypeValue = identity };
        }
        var storage = serialized.StackStorageType ?? serialized;
        return (storage.CanonicalName, argument.Value) switch
        {
            ("primitive:string", string value) => Result(AttributeArgumentKind.Text) with { StringValue = value },
            ("primitive:bool", bool value) => Result(AttributeArgumentKind.Boolean) with { SignedValue = value ? 1 : 0 },
            ("primitive:char", char value) => Result(AttributeArgumentKind.Character) with { UnsignedValue = value },
            ("primitive:i1", sbyte value) => Signed(value),
            ("primitive:i2", short value) => Signed(value),
            ("primitive:i4", int value) => Signed(value),
            ("primitive:i8", long value) => Signed(value),
            ("primitive:u1", byte value) => Unsigned(value),
            ("primitive:u2", ushort value) => Unsigned(value),
            ("primitive:u4", uint value) => Unsigned(value),
            ("primitive:u8", ulong value) => Unsigned(value),
            ("primitive:f4", float value) => Result(AttributeArgumentKind.Floating32) with { FloatingValue = value },
            ("primitive:f8", double value) => Result(AttributeArgumentKind.Floating64) with { FloatingValue = value },
            _ => throw Unsupported("custom attribute value does not match its serialized type"),
        };

        AttributeArgumentPlan Result(AttributeArgumentKind kind) => new(destination, kind) { SerializedType = serialized };
        AttributeArgumentPlan Signed(long value) => Result(AttributeArgumentKind.SignedInteger) with { SignedValue = value };
        AttributeArgumentPlan Unsigned(ulong value) => Result(AttributeArgumentKind.UnsignedInteger) with { UnsignedValue = value };
    }

    private static CompilerException Unsupported(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, message));
}
