using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Core.Tests.Types;

public sealed class CliTypeIdentityFactoryTests
{
    private static readonly AssemblyIdentity CoreLibrary = new("System.Private.CoreLib");

    public static TheoryData<string, CliValueKind, bool> PrimitiveFacades => new()
    {
        { "Void", CliValueKind.Void, true },
        { "Boolean", CliValueKind.I4, true },
        { "Char", CliValueKind.I4, true },
        { "SByte", CliValueKind.I4, true },
        { "Byte", CliValueKind.I4, true },
        { "Int16", CliValueKind.I4, true },
        { "UInt16", CliValueKind.I4, true },
        { "Int32", CliValueKind.I4, true },
        { "UInt32", CliValueKind.I4, true },
        { "Int64", CliValueKind.I8, true },
        { "UInt64", CliValueKind.I8, true },
        { "Single", CliValueKind.F4, true },
        { "Double", CliValueKind.F8, true },
        { "IntPtr", CliValueKind.NativeInt, true },
        { "UIntPtr", CliValueKind.NativeInt, true },
        { "String", CliValueKind.ManagedReference, false },
        { "Object", CliValueKind.ManagedReference, false },
        { "RuntimeTypeHandle", CliValueKind.I4, true },
        { "StringComparison", CliValueKind.I4, true },
    };

    [Theory]
    [MemberData(nameof(PrimitiveFacades))]
    public void NamedCanonicalizesCliPrimitiveFacades(
        string name,
        CliValueKind expectedStackKind,
        bool isValueType)
    {
        var identity = CliTypeIdentity.Named(CoreLibrary, "System", name, isValueType);

        Assert.Equal(expectedStackKind, identity.StackKind);
        Assert.Equal(expectedStackKind != CliValueKind.Void, identity.HasRuntimeStorage);
        Assert.Equal(isValueType, identity.IsValueType);
    }

    [Fact]
    public void NamedPreservesUnknownNamedTypeDefaults()
    {
        var reference = CliTypeIdentity.Named(CoreLibrary, "Example", "Reference", false);
        var value = CliTypeIdentity.Named(CoreLibrary, "Example", "Value", true);

        Assert.Equal(CliTypeShape.Named, reference.Shape);
        Assert.Equal(CliValueKind.ManagedReference, reference.StackKind);
        Assert.Equal(CliTypeShape.Named, value.Shape);
        Assert.Equal(CliValueKind.ValueType, value.StackKind);
    }

    [Fact]
    public void NamedAcceptsCompatibleExplicitStackKinds()
    {
        var canonicalPrimitive = CliTypeIdentity.Named(
            CoreLibrary,
            "System",
            "IntPtr",
            true,
            CliValueKind.NativeInt);
        var canonicalNamed = CliTypeIdentity.Named(
            CoreLibrary,
            "System",
            "RuntimeTypeHandle",
            true,
            CliValueKind.I4);
        var enumLike = CliTypeIdentity.Named(
            CoreLibrary,
            "Example",
            "Number",
            true,
            CliValueKind.I4);
        var reference = CliTypeIdentity.Named(
            CoreLibrary,
            "Example",
            "Reference",
            false,
            CliValueKind.ManagedReference);

        Assert.Equal(CliValueKind.NativeInt, canonicalPrimitive.StackKind);
        Assert.Equal(CliValueKind.I4, canonicalNamed.StackKind);
        Assert.Equal(CliValueKind.I4, enumLike.StackKind);
        Assert.Equal(CliValueKind.ManagedReference, reference.StackKind);
    }

    [Theory]
    [InlineData("System", "IntPtr", true, CliValueKind.ValueType)]
    [InlineData("System", "RuntimeTypeHandle", true, CliValueKind.ValueType)]
    [InlineData("Example", "Reference", false, CliValueKind.I4)]
    [InlineData("Example", "Value", true, CliValueKind.Void)]
    [InlineData("Example", "Value", true, CliValueKind.ManagedReference)]
    [InlineData("Example", "Value", true, CliValueKind.ManagedAddress)]
    [InlineData("Example", "Value", true, CliValueKind.Unknown)]
    public void NamedRejectsIncompatibleExplicitStackKinds(
        string @namespace,
        string name,
        bool isValueType,
        CliValueKind stackKind)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            CliTypeIdentity.Named(CoreLibrary, @namespace, name, isValueType, stackKind));

        Assert.Equal("stackKind", error.ParamName);
    }

    [Fact]
    public void FromDefinitionCanonicalizesPrimitiveAndEnumStorage()
    {
        var pointer = new TypeDefinitionModel(
            new EntityKey(CoreLibrary, 1),
            "System",
            "IntPtr",
            true,
            [],
            []);
        var enumType = new TypeDefinitionModel(
            new EntityKey(CoreLibrary, 2),
            "Example",
            "Number",
            true,
            [],
            [])
        {
            IsEnum = true,
            EnumUnderlyingType = CliTypeIdentity.Primitive("System.Int32", CliValueKind.I4, true),
        };

        var pointerIdentity = CliTypeIdentity.FromDefinition(pointer);
        var enumIdentity = CliTypeIdentity.FromDefinition(enumType);

        Assert.Equal(CliValueKind.NativeInt, pointerIdentity.StackKind);
        Assert.Equal(CliValueKind.I4, enumIdentity.StackKind);
        Assert.Equal(enumType.EnumUnderlyingType, enumIdentity.StackStorageType);
        Assert.Equal(CliValueKind.I4, enumType.EnumUnderlyingKind);
    }

    [Fact]
    public void FromDefinitionRejectsMissingDefinition()
    {
        Assert.Throws<ArgumentNullException>(() => CliTypeIdentity.FromDefinition(null!));
    }
}
