using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A unit names types by shape; read back, each must be the identity the compiler builds,
// stack and storage kinds included, and bytes that are not one are refused.
public sealed class UnitModelTypeTests
{
    private static readonly AssemblyIdentity Program = new("Program");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Point = CliTypeIdentity.Named(Program, "Shapes", "Point", isValueType: true);
    private static readonly CliTypeIdentity Modifier = CliTypeIdentity.Named(Program, "System.Runtime.CompilerServices", "IsVolatile", false);

    public static TheoryData<string> Shapes => new()
    {
        "primitive", "named", "nested", "szarray", "array", "byref", "pointer", "generic", "type parameter",
        "method parameter", "scoped parameter", "function pointer", "modreq", "modopt", "stack kind", "storage",
    };

    private static CliTypeIdentity Shape(string shape) => shape switch
    {
        "primitive" => Int32,
        "named" => Point,
        "nested" => CliTypeIdentity.Named(Program, "Shapes", "Outer+Inner", isValueType: false),
        "szarray" => CliTypeIdentity.SzArray(Point),
        "array" => CliTypeIdentity.Array(Point, 3),
        "byref" => CliTypeIdentity.ManagedByReference(Point),
        "pointer" => CliTypeIdentity.UnmanagedPointer(Int32),
        "generic" => CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Program, "Shapes", "Box`1", isValueType: false), [Point]),
        "type parameter" => CliTypeIdentity.GenericParameter(method: false, 0),
        "method parameter" => CliTypeIdentity.GenericParameter(method: true, 1),
        "scoped parameter" => CliTypeIdentity.ScopedGenericParameter("[Program]Shapes.Box`1", method: false, 0),
        "function pointer" => CliTypeIdentity.FunctionPointer(new(0, 0, 1,
            new MethodSignatureModel(CliValueKind.I4, [CliValueKind.I4]))),
        "modreq" => CliTypeIdentity.Modified(Int32, Modifier, isRequired: true),
        "modopt" => CliTypeIdentity.Modified(Int32, Modifier, isRequired: false),
        "stack kind" => Point.WithStackKind(CliValueKind.I8),
        _ => CliTypeIdentity.Named(Program, "Shapes", "Color", isValueType: true).WithStackStorageType(Int32),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ATypeOfEveryShapeReadsBackAsItWasWritten(string shape)
    {
        var type = Shape(shape);
        var writer = new UnitModelWriter();
        writer.Type(type);
        writer.Type(type);
        writer.Type(null);

        var reader = new UnitModelReader(writer.ToArray(), new NoMethods(), new NoFields());
        var read = reader.Type()!;

        Assert.True(UnitTypeComparer.Instance.Equals(type, read), $"{type.CanonicalName} read back as {read.CanonicalName}");
        Assert.Equal(type.Shape, read.Shape);
        Assert.Same(read, reader.Type());
        Assert.Null(reader.Type());
        Assert.True(reader.AtEnd);
    }

    [Fact]
    public void DefaultListsAreWrittenAsNoneOrEmpty()
    {
        var writer = new UnitModelWriter();
        writer.Ints(default);
        writer.Types(default);
        writer.Signature(new MethodSignatureModel(CliValueKind.Void, []) with { ParameterTypes = default });

        var reader = new UnitModelReader(writer.ToArray(), new NoMethods(), new NoFields());

        Assert.True(reader.Ints().IsDefault);
        Assert.Empty(reader.Types());
        Assert.Empty(reader.Signature().ParameterTypes);
    }

    [Fact]
    public void TypesAreComparedByShapeAndKinds()
    {
        Assert.True(UnitTypeComparer.Instance.Equals(null, null));
        Assert.False(UnitTypeComparer.Instance.Equals(Point, null));
        Assert.False(UnitTypeComparer.Instance.Equals(Point, Point.WithStackKind(CliValueKind.I8)));
        Assert.Equal(Point.GetHashCode(), UnitTypeComparer.Instance.GetHashCode(Point));
    }

    // Damage at each place a type is read from: its tag, its shape, its parts and its kinds.
    [Theory]
    [InlineData("unknown reference")]
    [InlineData("tag")]
    [InlineData("shape")]
    [InlineData("missing part")]
    [InlineData("value kind")]
    [InlineData("no name")]
    [InlineData("no assembly name")]
    [InlineData("no assembly")]
    [InlineData("truncated kinds")]
    [InlineData("truncated stack kind")]
    [InlineData("truncated")]
    public void BytesThatAreNotATypeAreRefused(string damage)
    {
        var writer = new UnitModelWriter();
        if (damage is "missing part" or "value kind") writer.Type(CliTypeIdentity.SzArray(Point));
        else if (damage == "no assembly") writer.Entity(new(Program, 1));
        else writer.Type(Point);
        var bytes = writer.ToArray();
        // A named type: tag, shape, assembly and name strings, value kind, stack kind, storage.
        var name = 4 + 1 + 4 + Program.Name.Length * 2;
        bytes = damage switch
        {
            "unknown reference" => Word(bytes, 0, 5),
            "tag" => Word(bytes, 0, -3),
            "shape" => [.. bytes[..4], 99, .. bytes[5..]],
            "missing part" => Word(bytes, 5, -1),
            // An array is a reference whatever its element.
            "value kind" => [.. bytes[..^6], 1, .. bytes[^5..]],
            "no name" => [.. Word(bytes, name, -1)[..(name + 4)], 1, (byte)CliValueKind.I4, 0xFF, 0xFF, 0xFF, 0xFF],
            // The assembly's string, after the tag and shape, written as none.
            "no assembly name" => [.. Word(bytes[..9], 5, -1), .. bytes[name..]],
            "no assembly" => Word(bytes, 0, -1),
            "truncated kinds" => bytes[..(bytes.Length - 6)],
            "truncated stack kind" => bytes[..(bytes.Length - 5)],
            _ => bytes[..^1],
        };
        var reader = new UnitModelReader(bytes, new NoMethods(), new NoFields());

        Assert.Throws<InvalidDataException>(() => damage == "no assembly" ? reader.Entity() : (object?)reader.Type());
    }

    // The writer has a form for every shape; a shape added without one fails here.
    [Fact]
    public void EveryShapeHasASample() =>
        Assert.Equal(Enum.GetValues<CliTypeShape>().Order(),
            Shapes.Select(shape => Shape(shape.Data).Shape).Distinct().Order());

    private static byte[] Word(byte[] bytes, int at, int value)
    {
        var copy = bytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(at), value);
        return copy;
    }

    private sealed class NoMethods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }

    private sealed class NoFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();
    }
}
