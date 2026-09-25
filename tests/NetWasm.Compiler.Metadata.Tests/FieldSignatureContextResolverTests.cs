using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class FieldSignatureContextResolverTests
{
    [Fact]
    public void ResolveUsesDeclaringGenericArguments()
    {
        var definition = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Fixtures",
            "Pair`2",
            true);
        var declaringType = CliTypeIdentity.GenericInstantiation(
            definition,
            [
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                CliTypeIdentity.Primitive("i8", CliValueKind.I8),
            ]);

        var context = Resolve(new FieldSignatureContextResolver(), declaringType);

        Assert.Equal(declaringType.TypeArguments, context.TypeArguments);
        Assert.Empty(context.MethodArguments);
    }

    [Fact]
    public void ResolveUsesEmptyContextForNonGenericDeclaringType()
    {
        var declaringType = CliTypeIdentity.Named(
            new AssemblyIdentity("in-memory"),
            "Fixtures",
            "Value",
            true);

        var context = Resolve(new FieldSignatureContextResolver(), declaringType);

        Assert.Empty(context.TypeArguments);
        Assert.Empty(context.MethodArguments);
    }

    private static CliGenericContext Resolve<TResolver>(
        TResolver resolver,
        CliTypeIdentity declaringType)
        where TResolver : IFieldSignatureContextResolver =>
        resolver.Resolve(declaringType);
}
