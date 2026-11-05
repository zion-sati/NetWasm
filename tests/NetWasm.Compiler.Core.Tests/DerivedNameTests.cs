using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class DerivedNameTests
{
    private static readonly AssemblyIdentity Assembly = new("Names");
    private static readonly CliTypeIdentity Owner = CliTypeIdentity.Named(Assembly, "Tests", "Owner", false);
    private static readonly CliTypeIdentity Other = CliTypeIdentity.Named(Assembly, "Tests", "Other", false);
    private static readonly MethodSignatureModel Signature = MethodSignatureModel.Create(CliValueKind.Void);

    [Fact]
    public void ACopyWithOtherPartsDerivesItsOwnCanonicalName()
    {
        var method = Method(0x06000001, []);
        var original = method.CanonicalName;

        var moved = method with { DeclaringType = Other };
        var renumbered = method with
        {
            Definition = method.Definition with { Key = new(Assembly, 0x06000002) },
        };
        var generic = method with { MethodArguments = [Owner] };

        Assert.Equal("[Names]Tests.Owner::0x06000001", original);
        Assert.Equal("[Names]Tests.Other::0x06000001", moved.CanonicalName);
        Assert.Equal("[Names]Tests.Owner::0x06000002", renumbered.CanonicalName);
        Assert.Equal("[Names]Tests.Owner::0x06000001<[Names]Tests.Owner>", generic.CanonicalName);
        Assert.Same(original, method.CanonicalName);
    }

    [Fact]
    public void CachedNamesTakeNoPartInEquality()
    {
        var named = Method(0x06000001, [Owner]);
        var unnamed = Method(0x06000001, named.MethodArguments) with { Definition = named.Definition };
        _ = named.CanonicalName;

        Assert.Equal(named, unnamed);
        Assert.Equal(named.GetHashCode(), unnamed.GetHashCode());
        // A copy keeps every part: equality, which ignores the cached name, sees them all.
        Assert.Equal(named, named with { });

        var type = new TypeDefinitionModel(new(Assembly, 0x02000001), "Tests", "Owner", false, [], []);
        var copy = type with { };
        Assert.Equal("Tests.Owner", type.FullName);
        Assert.Equal(type, copy);
        Assert.Equal("Tests.Renamed", (type with { Name = "Renamed" }).FullName);
        Assert.Equal("Global", (type with { Namespace = "", Name = "Global" }).FullName);
    }

    [Fact]
    public void FieldAndPropertyNamesFollowTheirCopies()
    {
        var field = new FieldInstanceModel(
            new FieldDefinitionModel(new(Assembly, 0x04000001), new(Assembly, 0x02000001), "Value",
                CliValueKind.I4, false),
            Owner,
            CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(new(Assembly, 0x17000001), new(Assembly, 0x02000001), "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4), [], null, null),
            Owner,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [],
            null,
            null);

        Assert.Equal("[Names]Tests.Owner::0x04000001", field.CanonicalName);
        Assert.Equal("[Names]Tests.Other::0x04000001", (field with { DeclaringType = Other }).CanonicalName);
        Assert.Equal("[Names]Tests.Owner::0x17000001", property.CanonicalName);
        Assert.Equal("[Names]Tests.Other::0x17000001", (property with { DeclaringType = Other }).CanonicalName);
        Assert.Equal(field, field with { });
        Assert.Equal(property, property with { });
        Assert.Same(field.CanonicalName, field.CanonicalName);
    }

    [Fact]
    public void PrimitivesAreShared()
    {
        Assert.Same(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        Assert.Same(
            CliTypeIdentity.Named(Assembly, "System", "Int32", true),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        Assert.NotSame(
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i4", CliValueKind.I8));
        Assert.Throws<ArgumentNullException>(() => CliTypeIdentity.Primitive(null!, CliValueKind.I4));
    }

    private static MethodInstanceModel Method(int token, ImmutableArray<CliTypeIdentity> arguments) => new(
        new MethodDefinitionModel(new(Assembly, token), new(Assembly, 0x02000001), "Run", true, Signature, 1),
        Owner,
        arguments,
        Signature);
}
