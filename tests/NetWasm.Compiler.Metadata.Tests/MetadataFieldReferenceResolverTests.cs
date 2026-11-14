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
    public void RuntimeTypeDefinitionsRequireTheAssemblies() =>
        Assert.Throws<ArgumentNullException>("assemblies", () => new RuntimeTypeDefinitions(null!));

    // A reader without runtime assemblies: no reference resolves through a forwarded type.
    private static readonly IRuntimeTypeDefinitions NoRuntimeTypes =
        new RuntimeTypeDefinitions(new Dictionary<string, MetadataAssemblySnapshot>());

    [Theory]
    [InlineData("match")]
    [InlineData("name")]
    [InlineData("signature")]
    [InlineData("static")]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    public void DefinitionTokensBindOnlyMatchingPlatformFields(string scenario)
    {
        var originalType = MetadataActorTestData.Type with { GenericArity = 1 };
        var original = MetadataActorTestData.Field with
        {
            Key = new(originalType.Key.Assembly, 0x04000001),
            SignatureType = CliTypeIdentity.GenericParameter(false, 0)
        };
        var platform = new AssemblyIdentity("Platform");
        var targetKey = new EntityKey(platform, 0x02000001);
        var target = original with { Key = new(platform, 0x04000001), DeclaringType = targetKey };
        target = scenario switch
        {
            "name" => target with { Name = "Other" },
            "signature" => target with { SignatureType = CliTypeIdentity.Primitive("i8", CliValueKind.I8) },
            "static" => target with { IsStatic = !original.IsStatic },
            _ => target
        };
        var alternate = target with { Key = new(platform, 0x04000002) };
        var targetType = originalType with
        {
            Key = targetKey,
            Fields = scenario == "missing" ? [] : scenario == "ambiguous" ? [target.Key, alternate.Key] : [target.Key]
        };
        var source = new MetadataAssemblySnapshot(originalType.Key.Assembly, null!,
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel> { [original.Key.MetadataToken] = original },
            new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>())
        {
            AssemblyIdentityAliases = new AssemblyIdentityAliases(ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<(AssemblyIdentity, string), AssemblyIdentity>.Empty
                    .Add((originalType.Key.Assembly, originalType.FullName), platform))
        };
        var contracts = new RecordingContractValidator();
        var resolver = new MetadataFieldReferenceResolver(new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(), new FieldSignatureContextResolver(), new TypeRepository(originalType),
            new FixedTypeDefinitionResolver(targetType), new FieldRepository(target, alternate),
            new IdentityStackTypeResolver(), contracts, NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());
        var context = new CliGenericContext([CliTypeIdentity.Primitive("i4", CliValueKind.I4)], []);
        if (scenario != "match")
        {
            var error = Assert.Throws<CompilerException>(() => resolver.Resolve(source,
                original.Key.MetadataToken, "Caller.Run", 7, context));
            Assert.Equal(scenario == "ambiguous" ? DiagnosticCode.AssemblyResolution : DiagnosticCode.MissingApi, error.Diagnostic.Code);
            Assert.Contains(original.Name, error.Message);
            Assert.Contains("Platform", error.Message);
            Assert.Null(contracts.Binding);
            return;
        }
        var result = resolver.Resolve(source, original.Key.MetadataToken, "Caller.Run", 7, context);
        Assert.Same(target, result.Definition);
        Assert.NotNull(contracts.Binding);
        Assert.Same(original, contracts.Binding.Original);
        Assert.Same(target, contracts.Binding.Target);
        Assert.Equal(platform, result.DeclaringType.ElementType!.Assembly);
        Assert.Equal("primitive:i4", Assert.Single(result.DeclaringType.TypeArguments).CanonicalName);
        Assert.Equal("primitive:i4", result.FieldType.CanonicalName);
    }

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());

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
            stackTypes, new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());

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
                new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess()));
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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());

        var resolved = resolver.Resolve(
            source,
            field.Key.MetadataToken,
            "Test.Method",
            0);

        Assert.Equal(expectedStackKind, resolved.DeclaringType.StackKind);
    }

    // A reference through the assembly's own type has no original contract to check.
    [Fact]
    public void AReferenceThroughTheAssemblysOwnTypeChecksNoOriginalContract()
    {
        var (provider, token) = OwnTypeReference("Value", [0x06, 0x08]);
        using var _ = provider;
        var resolver = new MetadataFieldReferenceResolver(new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(CliTypeIdentity.FromDefinition(MetadataActorTestData.Type)),
            new FieldSignatureContextResolver(), new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(MetadataActorTestData.Type), new FieldRepository(MetadataActorTestData.Field),
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes, UnrelatedContract,
            new UncheckedPrivateAccess());

        Assert.Same(MetadataActorTestData.Field, resolver.Resolve(OwnSource(provider), token, "Caller.Run", 0).Definition);
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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());
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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());
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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess());
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

    private sealed class UnusedContractValidator : IFrameworkFieldContractValidator
    {
        public void Validate(FrameworkFieldBinding binding) =>
            throw new InvalidOperationException("Unmapped fields must not inspect redirected contracts.");
    }

    private sealed class RecordingContractValidator : IFrameworkFieldContractValidator
    {
        public FrameworkFieldBinding? Binding { get; private set; }
        public void Validate(FrameworkFieldBinding binding) => Binding = binding;
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
    private sealed class UncheckedPrivateAccess : IMetadataCrossAssemblyPrivateAccessValidator
    {
        public void Validate(MetadataMemberAccessRequest request) { }
    }


    // A member reference may name its own assembly's type definition as its parent (ECMA-335 II.22.25).
    private static (MetadataReaderProvider Provider, int Token) OwnTypeReference(string name, byte[] signature)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Own.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var type = metadata.AddTypeDefinition(System.Reflection.TypeAttributes.Public,
            metadata.GetOrAddString(MetadataActorTestData.Type.Namespace), metadata.GetOrAddString(MetadataActorTestData.Type.Name),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var reference = metadata.AddMemberReference(type, metadata.GetOrAddString(name), metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        return (MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray()), MetadataTokens.GetToken(reference));
    }

    // Original contracts are present, but none declares the assembly's own type.
    private static readonly ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType> UnrelatedContract =
        ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty
            .Add((new("Contract"), "Other.Type"), new(null!, MetadataActorTestData.Type));

    private static MetadataAssemblySnapshot OwnSource(MetadataReaderProvider provider) =>
        new(MetadataActorTestData.Assembly, provider.GetMetadataReader(),
            new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, EntityHandle>());

}
