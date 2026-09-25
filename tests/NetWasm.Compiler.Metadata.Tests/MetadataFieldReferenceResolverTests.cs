using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataFieldReferenceResolverTests
{
    [Fact]
    public void ResolveBuildsAConstructedFieldDefinitionThroughItsInterface()
    {
        var type = MetadataActorTestData.Type with { GenericArity = 1 };
        var field = MetadataActorTestData.Field with
        {
            Key = MetadataActorTestData.Field.Key with { MetadataToken = 0x04000001 },
            DeclaringType = type.Key,
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel> { [type.Key.MetadataToken] = type },
            new Dictionary<int, FieldDefinitionModel> { [field.Key.MetadataToken] = field },
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataFieldReferenceResolver(
            new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(),
            new FieldSignatureContextResolver(),
            new TypeRepository(type),
            new UnusedTypeDefinitionResolver(),
            new FieldRepository(field),
            new IdentityStackTypeResolver());

        var resolved = ((IMetadataFieldReferenceResolver)resolver).Resolve(
            source,
            field.Key.MetadataToken,
            "Test.Method",
            0,
            new CliGenericContext([CliTypeIdentity.Primitive("i4", CliValueKind.I4)], []));

        Assert.True(resolved.IsConstructed);
        Assert.Equal(CliTypeShape.GenericInstantiation, resolved.DeclaringType.Shape);
        Assert.Equal("primitive:i4", Assert.Single(resolved.DeclaringType.TypeArguments).CanonicalName);
    }

    [Fact]
    public void ResolveNormalizesAFieldDefinitionStackTypeThroughItsStrategy()
    {
        var type = MetadataActorTestData.Type;
        var enumIdentity = CliTypeIdentity.Named(
            MetadataActorTestData.Assembly,
            "Test",
            "ExternalEnum",
            isValueType: true);
        var field = MetadataActorTestData.Field with
        {
            Key = MetadataActorTestData.Field.Key with { MetadataToken = 0x04000001 },
            DeclaringType = type.Key,
            FieldType = CliValueKind.ValueType,
            SignatureType = enumIdentity,
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel> { [type.Key.MetadataToken] = type },
            new Dictionary<int, FieldDefinitionModel> { [field.Key.MetadataToken] = field },
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var stackTypes = new FixedStackTypeResolver(CliValueKind.I8);
        var resolver = new MetadataFieldReferenceResolver(
            new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(),
            new FieldSignatureContextResolver(),
            new TypeRepository(type),
            new UnusedTypeDefinitionResolver(),
            new FieldRepository(field),
            stackTypes);

        var resolved = ((IMetadataFieldReferenceResolver)resolver).Resolve(
            source,
            field.Key.MetadataToken,
            "Test.Method",
            0);

        Assert.Equal(CliValueKind.I8, resolved.FieldType.StackKind);
        Assert.Equal(enumIdentity.CanonicalName, resolved.FieldType.CanonicalName);
        Assert.Same(enumIdentity, stackTypes.Input);
    }

    [Fact]
    public void ResolveRejectsUnsupportedMetadataTokenKind()
    {
        Action<IMetadataFieldReferenceResolver> contract = AssertUnsupportedMetadataTokenKind;
        contract(
            new MetadataFieldReferenceResolver(
                new UnsupportedHandleReader(),
                new UnusedSignatureTypeResolver(),
                new FieldSignatureContextResolver(),
                new UnusedTypeRepository(),
                new UnusedTypeDefinitionResolver(),
                new UnusedFieldRepository(),
                new IdentityStackTypeResolver()));
    }

    [Theory]
    [InlineData("Regular", false, CliValueKind.ValueType)]
    [InlineData("RuntimeTypeHandle", false, CliValueKind.I4)]
    [InlineData("StringComparison", false, CliValueKind.I4)]
    [InlineData("IntPtr", false, CliValueKind.NativeInt)]
    [InlineData("UIntPtr", false, CliValueKind.NativeInt)]
    [InlineData("EnumLike", true, CliValueKind.I4)]
    public void ResolveMapsSpecialDeclaringTypeStackKinds(
        string name,
        bool isEnum,
        CliValueKind expectedStackKind)
    {
        var type = MetadataActorTestData.Type with
        {
            Namespace = "System",
            Name = name,
            IsValueType = true,
            IsEnum = isEnum,
            EnumUnderlyingType = CliTypeIdentity.FromStackKind(expectedStackKind),
        };
        var field = MetadataActorTestData.Field with
        {
            Key = MetadataActorTestData.Field.Key with { MetadataToken = 0x04000001 },
            DeclaringType = type.Key,
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel> { [type.Key.MetadataToken] = type },
            new Dictionary<int, FieldDefinitionModel> { [field.Key.MetadataToken] = field },
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataFieldReferenceResolver(
            new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(),
            new FieldSignatureContextResolver(),
            new TypeRepository(type),
            new UnusedTypeDefinitionResolver(),
            new FieldRepository(field),
            new IdentityStackTypeResolver());

        var resolved = resolver.Resolve(
            source,
            field.Key.MetadataToken,
            "Test.Method",
            0);

        Assert.Equal(expectedStackKind, resolved.DeclaringType.StackKind);
    }

    [Fact]
    public void ResolveMatchesAndRejectsMemberReferenceFieldsThroughItsInterface()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var provider = new SignatureTypeProvider(assembly.Identity, assembly.Reader);
        var candidate = FindFieldReference(assembly, provider);
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var declaringIdentity = CliTypeIdentity.Named(
            assembly.Identity,
            candidate.DeclaringType.Namespace,
            candidate.DeclaringType.Name,
            candidate.DeclaringType.IsValueType);
        var matchingResolver = new MetadataFieldReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(declaringIdentity),
            new FieldSignatureContextResolver(),
            new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(candidate.DeclaringType with
            {
                Fields = [candidate.Field.Key],
            }),
            new FieldRepository(candidate.Field),
            new IdentityStackTypeResolver());

        var resolved = matchingResolver.Resolve(
            source,
            MetadataTokens.GetToken(candidate.Handle),
            "Test.Method",
            0);

        Assert.Same(candidate.Field, resolved.Definition);

        var genericResolver = new MetadataFieldReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(
                CliTypeIdentity.GenericInstantiation(declaringIdentity, [])),
            new FieldSignatureContextResolver(),
            new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(candidate.DeclaringType with
            {
                Fields = [candidate.Field.Key],
            }),
            new FieldRepository(candidate.Field),
            new IdentityStackTypeResolver());
        var genericResolved = genericResolver.Resolve(
            source,
            MetadataTokens.GetToken(candidate.Handle),
            "Test.Method",
            0);
        Assert.Same(candidate.Field, genericResolved.Definition);

        var nearMiss = candidate.Field with
        {
            Key = candidate.Field.Key with { MetadataToken = candidate.Field.Key.MetadataToken + 1 },
            Name = candidate.Field.Name + "Other",
        };
        var nearMissResolver = new MetadataFieldReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(declaringIdentity),
            new FieldSignatureContextResolver(),
            new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(candidate.DeclaringType with
            {
                Fields = [candidate.Field.Key, nearMiss.Key],
            }),
            new FieldRepository(candidate.Field, nearMiss),
            new IdentityStackTypeResolver());
        var nearMissResolved = nearMissResolver.Resolve(
            source,
            MetadataTokens.GetToken(candidate.Handle),
            "Test.Method",
            0);
        Assert.Same(candidate.Field, nearMissResolved.Definition);

        var rejectingResolver = new MetadataFieldReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(declaringIdentity),
            new FieldSignatureContextResolver(),
            new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(candidate.DeclaringType with { Fields = [] }),
            new UnusedFieldRepository(),
            new IdentityStackTypeResolver());
        Assert.Throws<CompilerException>(() => rejectingResolver.Resolve(
            source,
            MetadataTokens.GetToken(candidate.Handle),
            "Test.Method",
            0));
    }

    private static (MemberReferenceHandle Handle, FieldDefinitionModel Field,
        TypeDefinitionModel DeclaringType) FindFieldReference(
        ManagedAssembly assembly,
        SignatureTypeProvider provider)
    {
        foreach (var handle in assembly.Reader.MemberReferences)
        {
            var reference = assembly.Reader.GetMemberReference(handle);
            FieldDefinitionModel? field;
            try
            {
                var signature = reference.DecodeFieldSignature(provider, genericContext: null);
                var name = assembly.Reader.GetString(reference.Name);
                field = assembly.Fields.Values.FirstOrDefault(candidate =>
                    candidate.Name == name && candidate.SignatureType.Equals(signature));
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            if (field is not null)
            {
                return (
                    handle,
                    field,
                    assembly.Types[field.DeclaringType.MetadataToken]);
            }
        }
        throw new InvalidOperationException("The fixture must contain a resolvable field reference.");
    }

    private static void AssertUnsupportedMetadataTokenKind(IMetadataFieldReferenceResolver resolver)
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

    private sealed class UnusedSignatureTypeResolver : IMetadataSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle,
            CliGenericContext? genericContext = null) =>
            throw new InvalidOperationException("The unsupported-token path must not resolve a signature.");
    }

    private sealed class FixedSignatureTypeResolver(CliTypeIdentity result) :
        IMetadataSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(
            MetadataAssemblySnapshot source,
            EntityHandle handle,
            CliGenericContext? genericContext = null) => result;
    }

    private sealed class UnusedTypeRepository : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) =>
            throw new InvalidOperationException("The unsupported-token path must not read a type.");
    }

    private sealed class TypeRepository(TypeDefinitionModel type) : ITypeRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => type;
    }

    private sealed class UnusedTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new InvalidOperationException("The unsupported-token path must not resolve a type definition.");
    }

    private sealed class FixedTypeDefinitionResolver(TypeDefinitionModel result) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => result;
    }

    private sealed class UnusedFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new InvalidOperationException("The unsupported-token path must not read a field.");
    }

    private sealed class FieldRepository(
        FieldDefinitionModel field,
        FieldDefinitionModel? alternate = null) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            alternate is not null && key == alternate.Key ? alternate : field;
    }

    private sealed class IdentityStackTypeResolver : IMetadataStackTypeResolver
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

    private sealed class FixedStackTypeResolver(CliValueKind stackKind) :
        IMetadataStackTypeResolver
    {
        public CliTypeIdentity? Input { get; private set; }

        public CliTypeIdentity Resolve(CliTypeIdentity type)
        {
            Input = type;
            return type.WithStackKind(stackKind);
        }

        public MethodSignatureModel Resolve(MethodSignatureModel signature) =>
            throw new InvalidOperationException("Field resolution must normalize a type.");

        public CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext)
        {
            var context = genericContext.Normalize();
            return Resolve(type.Substitute(context.TypeArguments, context.MethodArguments));
        }

        public MethodSignatureModel Resolve(
            MethodSignatureModel signature,
            CliGenericContext genericContext)
        {
            var context = genericContext.Normalize();
            return Resolve(signature.Substitute(context.TypeArguments, context.MethodArguments));
        }
    }
}
