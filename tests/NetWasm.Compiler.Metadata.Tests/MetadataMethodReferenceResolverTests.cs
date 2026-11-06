using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.RuntimeProvidedMembers;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodReferenceResolverTests
{
    // A resolution is kept by its key: the same token of another assembly, or with other
    // type or method arguments, is another resolution.
    [Fact]
    public void AResolutionIsKeptByItsTokenAssemblyAndArguments()
    {
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var key = new MetadataMethodReferenceResolver.ResolutionKey("App", 0x0A000001, [int32], []);

        Assert.True(key.Equals(new MetadataMethodReferenceResolver.ResolutionKey("App", 0x0A000001, [int32], [])));
        Assert.False(key.Equals(new MetadataMethodReferenceResolver.ResolutionKey("Lib", 0x0A000001, [int32], [])));
        Assert.False(key.Equals(new MetadataMethodReferenceResolver.ResolutionKey("App", 0x0A000002, [int32], [])));
        Assert.False(key.Equals(new MetadataMethodReferenceResolver.ResolutionKey("App", 0x0A000001, [], [])));
        Assert.False(key.Equals(new MetadataMethodReferenceResolver.ResolutionKey("App", 0x0A000001, [int32], [int32])));
    }

    [Theory]
    [InlineData("inherited")]
    [InlineData("generic")]
    [InlineData("nearest")]
    [InlineData("constructor")]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("ambiguous")]
    public void ResolveFindsInheritedMethodThroughDerivedMemberReference(string scenario)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Inherited.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var parent = metadata.AddTypeReference(default,
            metadata.GetOrAddString("Tests"), metadata.GetOrAddString("Derived"));
        var baseHandle = metadata.AddTypeReference(default,
            metadata.GetOrAddString("Tests"), metadata.GetOrAddString("Base"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(0,
            result => result.Type().Int32(), _ => { });
        var reference = metadata.AddMemberReference(parent,
            metadata.GetOrAddString(scenario == "constructor" ? ".ctor" : "Run"), metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var baseType = MetadataActorTestData.Type with { Name = "Base", GenericArity = scenario == "generic" ? 1 : 0 };
        var derived = baseType with
        {
            Key = baseType.Key with { MetadataToken = 0x02000002 },
            Name = "Derived",
            Methods = []
        };
        var method = MetadataActorTestData.Method with
        {
            Name = scenario == "constructor" ? ".ctor" : "Run",
            Signature = scenario == "generic"
                ? new MethodSignatureModel(CliTypeIdentity.GenericParameter(false, 0), [])
                : MethodSignatureModel.Create(CliValueKind.I4)
        };
        var alternate = method with { Key = method.Key with { MetadataToken = 4 }, DeclaringType = derived.Key };
        if (scenario == "nearest") derived = derived with { Methods = [alternate.Key] };
        if (scenario == "missing" || scenario == "cycle") baseType = baseType with { Methods = [] };
        if (scenario == "ambiguous") baseType = baseType with { Methods = [method.Key, alternate.Key] };
        var derivedIdentity = CliTypeIdentity.FromDefinition(derived);
        var baseIdentity = scenario == "generic"
            ? CliTypeIdentity.GenericInstantiation(CliTypeIdentity.FromDefinition(baseType), [CliTypeIdentity.FromStackKind(CliValueKind.I4)])
            : CliTypeIdentity.FromDefinition(baseType);
        var source = new MetadataAssemblySnapshot(MetadataActorTestData.Assembly,
            provider.GetMetadataReader(),
            new Dictionary<int, TypeDefinitionModel>
            {
                [baseType.Key.MetadataToken] = baseType,
                [derived.Key.MetadataToken] = derived
            }, new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel> { [method.Key.MetadataToken] = method },
            new Dictionary<int, EntityHandle> { [derived.Key.MetadataToken] = baseHandle },
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataMethodReferenceResolver(new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(CliTypeIdentity.FromDefinition(derived)),
            new SignatureTypeComparer(), new TypeRepository(baseType),
            new InheritedTypeDefinitions(derived, baseType), new MethodRepository(method, alternate),
            new UnusedRuntimeProvidedMethodResolver(), new IdentityStackTypeResolver(),
            new FixedBaseTypes(derivedIdentity, scenario == "cycle" ? derivedIdentity : baseIdentity));
        if (scenario is "constructor" or "missing" or "cycle" or "ambiguous")
        {
            var failure = Assert.Throws<CompilerException>(() => resolver.Resolve(source,
                MetadataTokens.GetToken(reference), "Caller.Run", 0));
            Assert.Equal(DiagnosticCode.AssemblyResolution, failure.Diagnostic.Code);
            return;
        }
        var result = resolver.Resolve(source, MetadataTokens.GetToken(reference), "Caller.Run", 0);
        Assert.Same(scenario == "nearest" ? alternate : method, result.Definition);
        Assert.Equal(scenario == "nearest" ? derivedIdentity : baseIdentity, result.DeclaringType);
        Assert.Equal(CliValueKind.I4, result.Signature.ReturnType);
    }

    [Theory]
    [InlineData("Other", 0)]
    [InlineData("Run", 1)]
    [InlineData("Other", 1)]
    public void ResolveDoesNotAllocateSignaturesForUnrelatedNamesOrGenericArities(
        string unrelatedName,
        int unrelatedArity)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("References.dll"),
            metadata.GetOrAddGuid(new Guid("e3d7a597-20f6-4e76-aea5-0672f9a0913d")),
            default,
            default);
        var parent = metadata.AddTypeReference(
            default,
            metadata.GetOrAddString("Tests"),
            metadata.GetOrAddString("Target"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature().Parameters(
            0,
            result => result.Type().Int32(),
            _ => { });
        var reference = metadata.AddMemberReference(
            parent,
            metadata.GetOrAddString("Run"),
            metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var type = MetadataActorTestData.Type;
        var method = MetadataActorTestData.Method with
        {
            Name = "Run",
            GenericArity = 0,
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        };
        var unrelated = method with
        {
            Key = method.Key with { MetadataToken = method.Key.MetadataToken + 1 },
            Name = unrelatedName,
            GenericArity = unrelatedArity,
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            provider.GetMetadataReader(),
            new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var methods = new MethodRepository(method, unrelated);
        IMetadataMethodReferenceResolver CreateResolver(params EntityKey[] candidates) =>
            new MetadataMethodReferenceResolver(
                new MetadataEntityHandleReader(),
                new FixedSignatureTypeResolver(CliTypeIdentity.FromDefinition(type)),
                new SignatureTypeComparer(),
                new UnusedTypeRepository(),
                new FixedTypeDefinitionResolver(type with { Methods = [.. candidates] }),
                methods,
                new UnusedRuntimeProvidedMethodResolver(),
                new IdentityStackTypeResolver(), new NoBaseTypes());
        var control = CreateResolver(method.Key);
        var withUnrelated = CreateResolver(unrelated.Key, method.Key);
        var token = MetadataTokens.GetToken(reference);

        Assert.Same(method, control.Resolve(source, token, "Test.Method", 0).Definition);
        Assert.Same(method, withUnrelated.Resolve(source, token, "Test.Method", 0).Definition);
        // A resolver keeps its resolutions, so the search is measured on fresh resolvers.
        var freshControl = CreateResolver(method.Key);
        var freshUnrelated = CreateResolver(unrelated.Key, method.Key);
        var beforeControl = GC.GetAllocatedBytesForCurrentThread();
        var controlResult = freshControl.Resolve(source, token, "Test.Method", 0);
        var controlAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeControl;
        var beforeUnrelated = GC.GetAllocatedBytesForCurrentThread();
        var unrelatedResult = freshUnrelated.Resolve(source, token, "Test.Method", 0);
        var unrelatedAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeUnrelated;

        Assert.Same(method, controlResult.Definition);
        Assert.Same(method, unrelatedResult.Definition);
        Assert.Equal(controlResult.Signature, unrelatedResult.Signature);
        Assert.Equal(controlAllocation, unrelatedAllocation);
    }

    [Fact]
    public void ResolveBuildsAConstructedMethodDefinitionThroughItsInterface()
    {
        var type = MetadataActorTestData.Type with { GenericArity = 1 };
        var method = MetadataActorTestData.Method with
        {
            Key = MetadataActorTestData.Method.Key with { MetadataToken = 0x06000001 },
            DeclaringType = type.Key,
            GenericArity = 1,
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel> { [type.Key.MetadataToken] = type },
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel> { [method.Key.MetadataToken] = method },
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataMethodReferenceResolver(
            new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(),
            new UnusedSignatureComparer(),
            new TypeRepository(type),
            new UnusedTypeDefinitionResolver(),
            new UnusedMethodRepository(),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());

        var resolved = ((IMetadataMethodReferenceResolver)resolver).Resolve(
            source,
            method.Key.MetadataToken,
            "Test.Method",
            0,
            new CliGenericContext([CliTypeIdentity.Primitive("i4", CliValueKind.I4)], [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]));

        Assert.True(resolved.IsConstructed);
        Assert.Equal(CliTypeShape.GenericInstantiation, resolved.DeclaringType.Shape);
        Assert.Equal("primitive:i4", Assert.Single(resolved.DeclaringType.TypeArguments).CanonicalName);
        Assert.Single(resolved.MethodArguments);

        var openResolved = ((IMetadataMethodReferenceResolver)resolver).Resolve(
            source,
            method.Key.MetadataToken,
            "Test.Method",
            0,
            new CliGenericContext([CliTypeIdentity.Primitive("i4", CliValueKind.I4)], []));
        Assert.Empty(openResolved.MethodArguments);
    }

    [Fact]
    public void ResolveRejectsUnsupportedMetadataTokenKindThroughItsInterface()
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
        var resolver = new MetadataMethodReferenceResolver(
            new UnsupportedHandleReader(),
            new UnusedSignatureTypeResolver(),
            new UnusedSignatureComparer(),
            new UnusedTypeRepository(),
            new UnusedTypeDefinitionResolver(),
            new UnusedMethodRepository(),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());

        var exception = Assert.Throws<CompilerException>(() =>
            ((IMetadataMethodReferenceResolver)resolver).Resolve(
                source, 0, "Test.Method", 12));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
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
        var method = MetadataActorTestData.Method with
        {
            Key = MetadataActorTestData.Method.Key with { MetadataToken = 0x06000001 },
            DeclaringType = type.Key,
        };
        var source = new MetadataAssemblySnapshot(
            MetadataActorTestData.Assembly,
            (MetadataReader)RuntimeHelpers.GetUninitializedObject(typeof(MetadataReader)),
            new Dictionary<int, TypeDefinitionModel> { [type.Key.MetadataToken] = type },
            new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel> { [method.Key.MetadataToken] = method },
            new Dictionary<int, EntityHandle>(),
            new Dictionary<int, ImmutableArray<EntityHandle>>());
        var resolver = new MetadataMethodReferenceResolver(
            new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(),
            new UnusedSignatureComparer(),
            new TypeRepository(type),
            new UnusedTypeDefinitionResolver(),
            new MethodRepository(method),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());

        var resolved = resolver.Resolve(
            source,
            method.Key.MetadataToken,
            "Test.Method",
            0);

        Assert.Equal(expectedStackKind, resolved.DeclaringType.StackKind);
    }

    [Fact]
    public void ResolveSelectsTheMatchingGenericArityAndRejectsAnOtherwiseAmbiguousReference()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        Assert.True(assembly.Reader.GetTableRowCount(TableIndex.MethodSpec) > 0);
        var specificationHandle = Enumerable.Range(
                1,
                assembly.Reader.GetTableRowCount(TableIndex.MethodSpec))
            .Select(MetadataTokens.MethodSpecificationHandle)
            .First(handle => assembly.Reader.GetMethodSpecification(handle).Method.Kind ==
                HandleKind.MemberReference);
        var specification = assembly.Reader.GetMethodSpecification(specificationHandle);
        var reference = assembly.Reader.GetMemberReference(
            (MemberReferenceHandle)specification.Method);
        var referenceSignature = reference.DecodeMethodSignature(
            new SignatureTypeProvider(assembly.Identity, assembly.Reader),
            genericContext: null);
        var definition = assembly.Methods.Values.First(candidate =>
            candidate.Name == assembly.Reader.GetString(reference.Name));
        var methodForReference = definition with
        {
            Signature = new MethodSignatureModel(
                referenceSignature.ReturnType,
                referenceSignature.ParameterTypes),
        };
        var wrongArityMethod = methodForReference with
        {
            Key = new EntityKey(
                methodForReference.Key.Assembly,
                methodForReference.Key.MetadataToken + 1),
            GenericArity = methodForReference.GenericArity + 1,
        };
        var sameArityMethod = methodForReference with
        {
            Key = new EntityKey(
                methodForReference.Key.Assembly,
                methodForReference.Key.MetadataToken + 2),
        };
        var declaringType = assembly.Types[methodForReference.DeclaringType.MetadataToken];
        var declaringTypeWithOverloads = declaringType with
        {
            Methods = [wrongArityMethod.Key, methodForReference.Key],
        };
        var ambiguousDeclaringType = declaringType with
        {
            Methods = [sameArityMethod.Key, methodForReference.Key],
        };
        var source = new MetadataAssemblySnapshot(
            assembly.Identity,
            assembly.Reader,
            assembly.Types,
            assembly.Fields,
            assembly.Methods,
            assembly.Metadata.BaseTypes,
            assembly.Metadata.ImplementedInterfaces);
        var resolver = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(CliTypeIdentity.Named(
                assembly.Identity,
                declaringType.Namespace,
                declaringType.Name,
                declaringType.IsValueType)),
            new SignatureTypeComparer(),
            new TypeRepository(declaringTypeWithOverloads),
            new FixedTypeDefinitionResolver(
                declaringTypeWithOverloads),
            new MethodRepository(wrongArityMethod, methodForReference),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());

        var resolved = resolver.Resolve(
            source,
            MetadataTokens.GetToken(specificationHandle),
            "Test.Method",
            0);

        Assert.NotEmpty(resolved.MethodArguments);
        Assert.Equal(methodForReference.Key, resolved.Definition.Key);

        var genericResolver = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(
                    assembly.Identity,
                    declaringType.Namespace,
                    declaringType.Name,
                    declaringType.IsValueType),
                [])),
            new SignatureTypeComparer(),
            new TypeRepository(declaringTypeWithOverloads),
            new FixedTypeDefinitionResolver(
                declaringTypeWithOverloads),
            new MethodRepository(wrongArityMethod, methodForReference),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());
        var genericResolved = genericResolver.Resolve(
            source,
            MetadataTokens.GetToken(specificationHandle),
            "Test.Method",
            0);
        Assert.NotEmpty(genericResolved.MethodArguments);


        var ambiguousResolver = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(CliTypeIdentity.Named(
                assembly.Identity,
                declaringType.Namespace,
                declaringType.Name,
                declaringType.IsValueType)),
            new SignatureTypeComparer(),
            new TypeRepository(ambiguousDeclaringType),
            new FixedTypeDefinitionResolver(
                ambiguousDeclaringType),
            new MethodRepository(sameArityMethod, methodForReference),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());

        Assert.Throws<CompilerException>(() => ambiguousResolver.Resolve(
            source,
            MetadataTokens.GetToken(specificationHandle),
            "Test.Method",
            0));
    }

    [Fact]
    public void ResolveRejectsAReferencedMethodWhenNoCandidateMatches()
    {
        using var assets = TestAssets.Create();
        using var assembly = ManagedAssemblyTestFactory.Load(assets.Application);
        var handle = assembly.Reader.MemberReferences
            .First(candidate => assembly.Reader.GetString(
                assembly.Reader.GetMemberReference(candidate).Name) == "Adjust");
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
        var resolver = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(
                CliTypeIdentity.Named(MetadataActorTestData.Assembly, "Test", "Empty", false)),
            new UnusedSignatureComparer(),
            new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(emptyType),
            new UnusedMethodRepository(),
            new UnusedRuntimeProvidedMethodResolver(),
            new IdentityStackTypeResolver(), new NoBaseTypes());

        Assert.Throws<CompilerException>(() => resolver.Resolve(
            source,
            MetadataTokens.GetToken(handle),
            "Test.Method",
            0));


        var runtimeProvided = new MethodInstanceModel(default!, default!, [], default!);
        var runtimeResolver = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(
                CliTypeIdentity.Named(MetadataActorTestData.Assembly, "Test", "Empty", false)),
            new UnusedSignatureComparer(),
            new UnusedTypeRepository(),
            new FixedTypeDefinitionResolver(emptyType),
            new UnusedMethodRepository(),
            new FixedRuntimeProvidedMethodResolver(runtimeProvided),
            new IdentityStackTypeResolver(), new NoBaseTypes());
        var runtimeResolved = ((IMetadataMethodReferenceResolver)runtimeResolver).Resolve(
            source,
            MetadataTokens.GetToken(handle),
            "Test.Method",
            0);

        Assert.Equal(runtimeProvided, runtimeResolved);
    }

    private sealed class NoBaseTypes : IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) => null;
    }

    private sealed class FixedBaseTypes(CliTypeIdentity derived, CliTypeIdentity parent) : IBaseTypeIdentityResolver
    {
        public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type) => type.Equals(derived) ? parent : null;
    }

    private sealed class InheritedTypeDefinitions(TypeDefinitionModel derived, TypeDefinitionModel parent) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity type) =>
            type.FullName == derived.FullName ? derived : parent;
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

    private sealed class UnusedSignatureComparer : ISignatureTypeComparer
    {
        public bool Compare(CliTypeIdentity left, CliTypeIdentity right) =>
            throw new InvalidOperationException("The unsupported-token path must not compare signatures.");
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

    private sealed class UnusedMethodRepository : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) =>
            throw new InvalidOperationException("The unsupported-token path must not read a method.");
    }

    private sealed class MethodRepository(params MethodDefinitionModel[] methods) : IMethodRepository
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class UnusedRuntimeProvidedMethodResolver : IRuntimeProvidedMethodResolver
    {
        public MethodInstanceModel? Resolve(RuntimeProvidedMethodRequest request) => null;
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

    private sealed class FixedRuntimeProvidedMethodResolver(MethodInstanceModel result)
        : IRuntimeProvidedMethodResolver
    {
        public MethodInstanceModel? Resolve(RuntimeProvidedMethodRequest request) => result;
    }
}
