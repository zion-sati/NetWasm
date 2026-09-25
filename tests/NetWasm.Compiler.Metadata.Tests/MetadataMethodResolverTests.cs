using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodResolverTests
{
    [Fact]
    public void ResolveMatchesAReferencedMethodThroughItsInterface()
    {
        using var assets = TestAssets.Create();
        using var sourceAssembly = ManagedAssemblyTestFactory.Load(assets.Application);
        using var libraryAssembly = ManagedAssemblyTestFactory.Load(assets.Library);
        var referenceHandle = sourceAssembly.Reader.MemberReferences.Single(handle =>
            sourceAssembly.Reader.GetString(
                sourceAssembly.Reader.GetMemberReference(handle).Name) == "Adjust");
        var method = libraryAssembly.Methods.Values.Single(candidate =>
            candidate.Name == "Adjust");
        var declaringType = libraryAssembly.Types[method.DeclaringType.MetadataToken];
        var member = sourceAssembly.Reader.GetMemberReference(referenceHandle);
        var referenceSignature = member.DecodeMethodSignature(
            new SignatureTypeProvider(sourceAssembly.Identity, sourceAssembly.Reader),
            genericContext: null);
        var methodForReference = method with
        {
            Signature = new MethodSignatureModel(
                referenceSignature.ReturnType,
                referenceSignature.ParameterTypes),
        };
        var source = new MetadataAssemblySnapshot(
            sourceAssembly.Identity,
            sourceAssembly.Reader,
            sourceAssembly.Types,
            sourceAssembly.Fields,
            sourceAssembly.Methods,
            sourceAssembly.Metadata.BaseTypes,
            sourceAssembly.Metadata.ImplementedInterfaces);
        var resolver = new MetadataMethodResolver(
            new MetadataEntityHandleReader(),
            new FixedTypeResolver(declaringType),
            new SelectiveMethodRepository(methodForReference));

        var resolved = ((IMetadataMethodResolver)resolver).Resolve(
            source,
            MetadataTokens.GetToken(referenceHandle),
            "Test.Method",
            0);

        Assert.Same(methodForReference, resolved);
    }

    [Fact]
    public void ResolveReturnsMethodDefinitionThroughItsInterface()
    {
        var method = MetadataActorTestData.Method with
        {
            Key = MetadataActorTestData.Method.Key with { MetadataToken = 0x06000001 },
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel> { [method.Key.MetadataToken] = method },
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataMethodResolver(
            new DefinitionHandleReader(),
            new UnusedTypeResolver(),
            new MethodRepository(method));

        var resolved = ((IMetadataMethodResolver)resolver).Resolve(
            source, method.Key.MetadataToken, "Test.Method", 0);

        Assert.Same(method, resolved);
    }

    [Fact]
    public void ResolveRejectsUnsupportedMetadataTokenKind()
    {
        Action<IMetadataMethodResolver> contract = AssertUnsupportedMetadataTokenKind;
        contract(
            new MetadataMethodResolver(
                new UnsupportedHandleReader(),
                new UnusedTypeResolver(),
                new UnusedMethodRepository()));
    }

    [Fact]
    public void ResolveRejectsAReferencedMethodWhenNoCandidateMatches()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.Application);
        var handle = assembly.Reader.MemberReferences.First();
        var emptyType = new TypeDefinitionModel(
            MetadataActorTestData.TypeKey,
            "Test",
            "Empty",
            false,
            [],
            []);
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var resolver = new MetadataMethodResolver(
            new MetadataEntityHandleReader(),
            new FixedTypeResolver(emptyType),
            new UnusedMethodRepository());

        Assert.Throws<CompilerException>(() => resolver.Resolve(
            source,
            MetadataTokens.GetToken(handle),
            "Test.Method",
            0));
    }

    private static void AssertUnsupportedMetadataTokenKind(IMetadataMethodResolver resolver)
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

        Assert.Throws<CompilerException>(() => resolver.Resolve(source, 0, "Test.Method", 12));
    }

    private sealed class UnsupportedHandleReader : IMetadataEntityHandleReader
    {
        public EntityHandle Read(int token, string kind, string method, int ilOffset) => default;
    }

    private sealed class DefinitionHandleReader : IMetadataEntityHandleReader
    {
        public EntityHandle Read(int token, string kind, string method, int ilOffset) =>
            MetadataTokens.EntityHandle(0x06000001);
    }

    private sealed class UnusedTypeResolver : IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(MetadataAssemblySnapshot source, EntityHandle handle) =>
            throw new InvalidOperationException("The unsupported-token path must not resolve a type.");
    }

    private sealed class UnusedMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new InvalidOperationException("The unsupported-token path must not read a method.");
    }

    private sealed class MethodRepository(MethodDefinitionModel method) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => method;
    }

    private sealed class SelectiveMethodRepository(MethodDefinitionModel method) : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) => key == method.Key
            ? method
            : method with { Name = "<other>" };
    }

    private sealed class FixedTypeResolver(TypeDefinitionModel type) : IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(MetadataAssemblySnapshot source, EntityHandle handle) => type;
    }
}
