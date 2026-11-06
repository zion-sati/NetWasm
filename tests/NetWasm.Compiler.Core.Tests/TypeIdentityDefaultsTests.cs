using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// Identities read from metadata may name no namespace, no assembly or no arguments; each
// reads as the empty one, so equal types share one identity.
public sealed class TypeIdentityDefaultsTests
{
    [Fact]
    public void AMissingNamespaceOrAssemblyNameIsTheEmptyOne()
    {
        Assert.Same(CliTypeIdentity.Named(new AssemblyIdentity(""), "", "Point", isValueType: true),
            CliTypeIdentity.Named(new AssemblyIdentity(null!), null!, "Point", isValueType: true));
    }

    [Fact]
    public void AMissingArgumentListIsTheEmptyOne()
    {
        var generic = CliTypeIdentity.Named(new AssemblyIdentity("Program"), "", "Box`1", isValueType: false);
        var signature = new MethodSignatureModel(CliTypeIdentity.Primitive("void", CliValueKind.Void), default);

        Assert.Empty(CliTypeIdentity.GenericInstantiation(generic, default).TypeArguments);
        Assert.Empty(signature.ParameterSignatureTypes);
        Assert.Empty(signature.ParameterTypes);
    }

    [Fact]
    public void AnEntityOfAnUnnamedAssemblyHashesByItsToken() =>
        Assert.Equal(new EntityKey(new AssemblyIdentity(""), 0x02000001).GetHashCode(),
            new EntityKey(new AssemblyIdentity(""), 0x02000001).GetHashCode());
}
