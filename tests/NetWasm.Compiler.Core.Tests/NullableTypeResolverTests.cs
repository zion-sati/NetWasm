using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Core.Tests;

public sealed class NullableTypeResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("in-memory");

    [Fact]
    public void ResolveReturnsTheSingleNullableTypeArgument()
    {
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
            [underlying]);
        var resolver = AsResolver();

        Assert.Same(underlying, resolver.Resolve(nullable));
    }

    [Fact]
    public void ResolveRejectsNonNullableAndMalformedNullableTypes()
    {
        var resolver = AsResolver();
        var malformed = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
            []);
        var otherGeneric = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", "ValueTuple`1", true),
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);

        Assert.Null(resolver.Resolve(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        Assert.Null(resolver.Resolve(otherGeneric));
        Assert.Null(resolver.Resolve(malformed));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
    }

    private static INullableTypeResolver AsResolver() =>
        new[] { new NullableTypeResolver() }
            .Cast<INullableTypeResolver>()
            .Single();
}
