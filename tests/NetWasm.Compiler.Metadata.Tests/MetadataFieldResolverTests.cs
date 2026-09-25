using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataFieldResolverTests
{
    [Fact]
    public void ResolveMatchesAReferencedFieldThroughItsInterface()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);
        var candidate = assembly.Reader.MemberReferences
            .Select(handle => TryGetFieldReference(assembly, provider, handle))
            .OfType<FieldReferenceCandidate>()
            .FirstOrDefault();
        Assert.NotNull(candidate);
        var fieldForReference = candidate.Field with { SignatureType = candidate.SignatureType };
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var resolver = new MetadataFieldResolver(
            new MetadataEntityHandleReader(),
            new FixedTypeResolver(candidate.DeclaringType),
            new SelectiveFieldRepository(fieldForReference));

        var resolved = ((IMetadataFieldResolver)resolver).Resolve(
            source,
            MetadataTokens.GetToken(candidate.Handle),
            "Test.Method",
            0);

        Assert.Same(fieldForReference, resolved);
    }

    [Fact]
    public void ResolveReturnsFieldDefinitionThroughItsInterface()
    {
        var field = MetadataActorTestData.Field with
        {
            Key = MetadataActorTestData.Field.Key with { MetadataToken = 0x04000001 },
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel> { [field.Key.MetadataToken] = field },
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataFieldResolver(
            new DefinitionHandleReader(),
            new UnusedTypeResolver(),
            new FieldRepository(field));

        var resolved = ((IMetadataFieldResolver)resolver).Resolve(
            source, field.Key.MetadataToken, "Test.Method", 0);

        Assert.Same(field, resolved);
    }

    [Fact]
    public void ResolveRejectsUnsupportedMetadataTokenKind()
    {
        Action<IMetadataFieldResolver> contract = AssertUnsupportedMetadataTokenKind;
        contract(
            new MetadataFieldResolver(
                new UnsupportedHandleReader(),
                new UnusedTypeResolver(),
                new UnusedFieldRepository()));
    }

    [Fact]
    public void ResolveRejectsAReferencedFieldWhenNoCandidateMatches()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);
        var handle = assembly.Reader.MemberReferences
            .First(candidate => TryGetFieldReference(assembly, provider, candidate) is not null);
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
        var resolver = new MetadataFieldResolver(
            new MetadataEntityHandleReader(),
            new FixedTypeResolver(emptyType),
            new UnusedFieldRepository());

        Assert.Throws<CompilerException>(() => resolver.Resolve(
            source,
            MetadataTokens.GetToken(handle),
            "Test.Method",
            0));
    }

    private static void AssertUnsupportedMetadataTokenKind(IMetadataFieldResolver resolver)
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
            MetadataTokens.EntityHandle(0x04000001);
    }

    private sealed class UnusedTypeResolver : IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(MetadataAssemblySnapshot source, EntityHandle handle) =>
            throw new InvalidOperationException("The unsupported-token path must not resolve a type.");
    }

    private sealed class UnusedFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new InvalidOperationException("The unsupported-token path must not read a field.");
    }

    private sealed class FieldRepository(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => field;
    }

    private sealed class SelectiveFieldRepository(FieldDefinitionModel field) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => key == field.Key
            ? field
            : field with { Name = "<other>" };
    }

    private sealed class FixedTypeResolver(TypeDefinitionModel type) : IMetadataTypeResolver
    {
        public TypeDefinitionModel Resolve(MetadataAssemblySnapshot source, EntityHandle handle) => type;
    }

    private sealed record FieldReferenceCandidate(
        MemberReferenceHandle Handle,
        TypeDefinitionModel DeclaringType,
        FieldDefinitionModel Field,
        CliTypeIdentity SignatureType);

    private static FieldReferenceCandidate? TryGetFieldReference(
        ManagedAssembly assembly,
        SignatureTypeProvider provider,
        MemberReferenceHandle handle)
    {
        FieldReferenceCandidate? candidate;
        try
        {
            var reference = assembly.Reader.GetMemberReference(handle);
            var signature = reference.DecodeFieldSignature(provider, genericContext: null);
            var name = assembly.Reader.GetString(reference.Name);
            var match = assembly.Types.Values
                .SelectMany(type => type.Fields.Select(key =>
                    (Type: type, Field: assembly.Fields[key.MetadataToken])))
                .FirstOrDefault(pair =>
                    pair.Field.Name == name && pair.Field.SignatureType.Equals(signature));
            candidate = match.Field is null
                ? null
                : new FieldReferenceCandidate(handle, match.Type, match.Field, signature);
        }
        catch (BadImageFormatException)
        {
            candidate = null;
        }
        return candidate;
    }
}
