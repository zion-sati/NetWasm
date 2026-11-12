using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// A type's flag word in the runtime type table: CoreLib's flags, the array rank and the TypeCode.
public sealed class RuntimeTypeFactsEncodingTests
{
    private static readonly AssemblyIdentity Assembly = new("type-facts");

    [Fact]
    public void AnEnumIsASealedValueTypeDefinitionWithItsUnderlyingTypeCode()
    {
        var definition = Definition("Fixtures", "Number", isValueType: true) with
        {
            IsEnum = true,
            IsSealed = true,
            EnumUnderlyingType = CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        };

        var word = Word(CliTypeIdentity.Named(Assembly, "Fixtures", "Number", isValueType: true, CliValueKind.I4), definition);

        Assert.Equal(RuntimeTypeFactsFlags.ValueType | RuntimeTypeFactsFlags.Enum | RuntimeTypeFactsFlags.Sealed |
            RuntimeTypeFactsFlags.TypeDefinition, Flags(word));
        Assert.Equal(8, TypeCode(word));
        Assert.Equal(0, Rank(word));
    }

    // A generic parameter is a class unless its owner gives it the struct constraint, which makes it a
    // value type; its variance and special constraints take the rank field, as it has no rank. An owner
    // without parameter data, or none at all, leaves them clear.
    [Theory]
    [InlineData(-1, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(1, 0x04, false)]
    [InlineData(2, 0x18, true)]
    [InlineData(3, 0x01, false)]
    [InlineData(5, 0, false)]
    public void AGenericParameterTakesItsOwnersAttributes(int index, int attributes, bool valueType)
    {
        var owner = Definition("Fixtures", "Holder`5", isValueType: false) with
        {
            GenericArity = 5,
            GenericParameterAttributes = [0, 0x04, 0x18, 0x01, 0x20],
        };

        var word = Word(CliTypeIdentity.ScopedGenericParameter("owner", method: false, index: Math.Max(index, 0)),
            definition: null, index < 0 ? null : owner);

        Assert.Equal(RuntimeTypeFactsFlags.ContainsGenericParameters | RuntimeTypeFactsFlags.GenericTypeParameter |
            (valueType ? RuntimeTypeFactsFlags.ValueType : RuntimeTypeFactsFlags.Class), Flags(word));
        Assert.Equal(attributes, Rank(word));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARefStructIsByRefLike(bool byRefLike)
    {
        var identity = CliTypeIdentity.Named(Assembly, "Fixtures", "Window", isValueType: true);

        var word = Word(identity, Definition("Fixtures", "Window", isValueType: true) with { IsByRefLike = byRefLike });

        Assert.Equal(byRefLike, (Flags(word) & RuntimeTypeFactsFlags.ByRefLike) != 0);
    }

    [Fact]
    public void RejectsMissingDefinitionForDefinitionBackedIdentity()
    {
        var exception = Assert.Throws<CompilerException>(() => Word(
            CliTypeIdentity.Named(Assembly, "Fixtures", "Box`1", false), definition: null));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void AClosedNullableIsAConstructedValueTypeAndNoTypeDefinition()
    {
        var definition = Definition("System", "Nullable`1", isValueType: true) with { GenericArity = 1, IsSealed = true };
        var identity = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", isValueType: true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);

        Assert.Equal(RuntimeTypeFactsFlags.ValueType | RuntimeTypeFactsFlags.GenericType |
            RuntimeTypeFactsFlags.Sealed | RuntimeTypeFactsFlags.Nullable, Flags(Word(identity, definition)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AbstractFollowsTheDefinition(bool abstractDefinition, bool expected)
    {
        var definition = Definition("Fixtures", "Base", isValueType: false) with { IsAbstract = abstractDefinition };

        var flags = Flags(Word(CliTypeIdentity.Named(Assembly, "Fixtures", "Base", isValueType: false), definition));

        Assert.Equal(expected, (flags & RuntimeTypeFactsFlags.Abstract) != 0);
    }

    [Theory]
    [InlineData("bool", CliValueKind.I4, 3)]
    [InlineData("char", CliValueKind.I4, 4)]
    [InlineData("i1", CliValueKind.I4, 5)]
    [InlineData("u1", CliValueKind.I4, 6)]
    [InlineData("i2", CliValueKind.I4, 7)]
    [InlineData("u2", CliValueKind.I4, 8)]
    [InlineData("i4", CliValueKind.I4, 9)]
    [InlineData("u4", CliValueKind.I4, 10)]
    [InlineData("i8", CliValueKind.I8, 11)]
    [InlineData("u8", CliValueKind.I8, 12)]
    [InlineData("f4", CliValueKind.F4, 13)]
    [InlineData("f8", CliValueKind.F8, 14)]
    [InlineData("string", CliValueKind.ManagedReference, 18)]
    public void EveryPrimitiveHasItsTypeCode(string name, CliValueKind kind, int expected)
    {
        var identity = CliTypeIdentity.Primitive(name, kind, isValueType: name != "string");

        Assert.Equal(expected, TypeCode(Word(identity, Definition("System", "Primitive", identity.IsValueType))));
    }

    [Theory]
    [InlineData("System", "DBNull", false, 2)]
    [InlineData("System", "Decimal", true, 15)]
    [InlineData("System", "DateTime", true, 16)]
    public void SpecialNamedTypesHaveTheirTypeCodes(string @namespace, string name, bool isValueType, int expected)
    {
        var word = Word(CliTypeIdentity.Named(Assembly, @namespace, name, isValueType),
            Definition(@namespace, name, isValueType));

        Assert.Equal(expected, TypeCode(word));
    }

    [Theory]
    [InlineData(false, (int)(RuntimeTypeFactsFlags.ByReference | RuntimeTypeFactsFlags.Class))]
    [InlineData(true, (int)(RuntimeTypeFactsFlags.Pointer | RuntimeTypeFactsFlags.Class))]
    public void ByReferenceAndPointerTypesTakeNoDefinitionFlags(bool unmanaged, int expected)
    {
        var element = CliTypeIdentity.Named(Assembly, "Fixtures", "Value", isValueType: true);
        var definition = Definition("Fixtures", "Value", isValueType: true) with
        {
            IsEnum = true,
            IsInterface = true,
            IsSealed = true,
            EnumUnderlyingType = CliTypeIdentity.Primitive("u2", CliValueKind.I4),
        };

        var word = Word(unmanaged ? CliTypeIdentity.UnmanagedPointer(element) : CliTypeIdentity.ManagedByReference(element),
            definition);

        Assert.Equal(expected, (int)Flags(word));
        Assert.Equal(1, TypeCode(word));
    }

    [Theory]
    [InlineData("i4", CliValueKind.I4, true,
        (int)(RuntimeTypeFactsFlags.ValueType | RuntimeTypeFactsFlags.Sealed | RuntimeTypeFactsFlags.TypeDefinition))]
    [InlineData("string", CliValueKind.ManagedReference, false,
        (int)(RuntimeTypeFactsFlags.Class | RuntimeTypeFactsFlags.Sealed | RuntimeTypeFactsFlags.TypeDefinition))]
    public void APrimitiveIsADefinitionBackedTypeDefinition(
        string name, CliValueKind kind, bool isValueType, int expected)
    {
        var definition = Definition("System", isValueType ? "Int32" : "String", isValueType) with { IsSealed = true };

        Assert.Equal(expected, (int)Flags(Word(CliTypeIdentity.Primitive(name, kind, isValueType), definition)));
    }

    [Theory]
    [InlineData(0, (int)(RuntimeTypeFactsFlags.ContainsGenericParameters | RuntimeTypeFactsFlags.GenericTypeParameter |
        RuntimeTypeFactsFlags.Class))]
    [InlineData(1, (int)(RuntimeTypeFactsFlags.ContainsGenericParameters | RuntimeTypeFactsFlags.GenericTypeDefinition |
        RuntimeTypeFactsFlags.GenericType | RuntimeTypeFactsFlags.Class | RuntimeTypeFactsFlags.TypeDefinition))]
    [InlineData(2, (int)(RuntimeTypeFactsFlags.Interface | RuntimeTypeFactsFlags.TypeDefinition))]
    [InlineData(3, (int)(RuntimeTypeFactsFlags.Class | RuntimeTypeFactsFlags.TypeDefinition))]
    [InlineData(4, (int)(RuntimeTypeFactsFlags.ContainsGenericParameters | RuntimeTypeFactsFlags.GenericMethodParameter |
        RuntimeTypeFactsFlags.Class))]
    public void GenericParametersDefinitionsInterfacesAndClassesAreClassified(int kind, int expected)
    {
        var name = kind == 1 ? "Box`1" : kind == 2 ? "IMarker" : "Reference";
        var identity = kind switch
        {
            0 => CliTypeIdentity.GenericParameter(method: false, 0),
            4 => CliTypeIdentity.GenericParameter(method: true, 0),
            _ => CliTypeIdentity.Named(Assembly, "Fixtures", name, isValueType: false),
        };
        var definition = Definition("Fixtures", name, isValueType: false) with
        {
            GenericArity = kind == 1 ? 1 : 0,
            IsInterface = kind == 2,
        };

        Assert.Equal(expected, (int)Flags(Word(identity, definition)));
    }

    // GetArrayRank reads the rank from the word: a vector has rank 1, a rectangular array its rank.
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    public void ArraysCarryTheirRank(int rank, bool vector)
    {
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var identity = vector ? CliTypeIdentity.SzArray(element) : CliTypeIdentity.Array(element, rank);

        var word = Word(identity, Definition("System", "Array", isValueType: false) with { IsAbstract = true });

        Assert.Equal(rank, Rank(word));
        Assert.Equal(vector, (Flags(word) & RuntimeTypeFactsFlags.SzArray) != 0);
        Assert.Equal(0, (int)(Flags(word) & RuntimeTypeFactsFlags.TypeDefinition));
    }

    private static uint Word(CliTypeIdentity identity, TypeDefinitionModel? definition, TypeDefinitionModel? owner = null) =>
        RuntimeTypeFactsEncoding.Word(identity, definition, owner);

    private static RuntimeTypeFactsFlags Flags(uint word) =>
        (RuntimeTypeFactsFlags)(word & ~(0x3fu << RuntimeTypeFactsEncoding.ArrayRankShift) &
            ((1u << RuntimeTypeFactsEncoding.TypeCodeShift) - 1));

    private static int Rank(uint word) => (int)(word >> RuntimeTypeFactsEncoding.ArrayRankShift & 0x3f);

    private static int TypeCode(uint word) => (int)(word >> RuntimeTypeFactsEncoding.TypeCodeShift);

    private static TypeDefinitionModel Definition(string @namespace, string name, bool isValueType) =>
        new(new EntityKey(Assembly, 1), @namespace, name, isValueType, [], []);
}
