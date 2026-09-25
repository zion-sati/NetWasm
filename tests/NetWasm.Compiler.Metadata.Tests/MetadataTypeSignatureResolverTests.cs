using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeSignatureResolverTests
{
    [Fact]
    public void ResolveRejectsNilMetadataToken()
    {
        Action<IMetadataTypeSignatureResolver> contract = AssertNilTokenRejected;
        contract(
            new MetadataTypeSignatureResolver(
                new UnusedSignatureResolver(),
                new PassthroughStackTypeResolver()));
    }

    [Fact]
    public void ResolvePassesThroughAnExplicitGenericContext()
    {
        var expected = CliTypeIdentity.GenericParameter(method: false, 0);
        var resolver = new MetadataTypeSignatureResolver(
            new StubSignatureResolver(expected),
            new PassthroughStackTypeResolver());
        var reader = (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader));
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            reader,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());

        var actual = ((IMetadataTypeSignatureResolver)resolver).Resolve(
            source,
            MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(1)),
            new CliGenericContext([expected], []));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void ResolveUsesAnEmptyGenericContextWhenNoneIsProvided()
    {
        var expected = CliTypeIdentity.Named(
            MetadataActorTestData.Assembly,
            "Test.Namespace",
            "Sample",
            false);
        var resolver = new MetadataTypeSignatureResolver(
            new StubSignatureResolver(expected),
            new PassthroughStackTypeResolver());
        var reader = (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader));
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            reader,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());

        var actual = ((IMetadataTypeSignatureResolver)resolver).Resolve(
            source,
            MetadataTokens.GetToken(MetadataTokens.TypeDefinitionHandle(1)));

        Assert.Same(expected, actual);
    }

    private static void AssertNilTokenRejected(IMetadataTypeSignatureResolver resolver)
    {
        var reader = (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader));
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            reader,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());

        Assert.Throws<CompilerException>(() => resolver.Resolve(source, 0));
    }

    private sealed class UnusedSignatureResolver : IMetadataSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle,
            CliGenericContext? genericContext = null) =>
            throw new InvalidOperationException("A nil token must not resolve a signature.");
    }

    private sealed class StubSignatureResolver(CliTypeIdentity result) :
        IMetadataSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle,
            CliGenericContext? genericContext = null) => result;
    }

    private sealed class PassthroughStackTypeResolver : IMetadataStackTypeResolver
    {
        public CliTypeIdentity Resolve(CliTypeIdentity type) => type;

        public CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext)
        {
            var context = genericContext.Normalize();
            return Resolve(type.Substitute(context.TypeArguments, context.MethodArguments));
        }

        public MethodSignatureModel Resolve(MethodSignatureModel signature) => signature;

        public MethodSignatureModel Resolve(
            MethodSignatureModel signature,
            CliGenericContext genericContext)
        {
            var context = genericContext.Normalize();
            return Resolve(signature.Substitute(context.TypeArguments, context.MethodArguments));
        }
    }
}
