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
    // A reader without runtime assemblies: no reference resolves through a forwarded type.
    private static readonly IRuntimeTypeDefinitions NoRuntimeTypes =
        new RuntimeTypeDefinitions(new Dictionary<string, MetadataAssemblySnapshot>());

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
            new UnusedRuntimeProvidedMethodResolver(), new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(),
            new FixedBaseTypes(derivedIdentity, scenario == "cycle" ? derivedIdentity : baseIdentity));
        if (scenario is "constructor" or "missing" or "cycle" or "ambiguous")
        {
            var failure = Assert.Throws<CompilerException>(() => resolver.Resolve(source,
                MetadataTokens.GetToken(reference), "Caller.Run", 0));
            Assert.Equal(scenario == "ambiguous" ? DiagnosticCode.AssemblyResolution : DiagnosticCode.MissingApi, failure.Diagnostic.Code);
            return;
        }
        var result = resolver.Resolve(source, MetadataTokens.GetToken(reference), "Caller.Run", 0);
        Assert.Same(scenario == "nearest" ? alternate : method, result.Definition);
        Assert.Equal(scenario == "nearest" ? derivedIdentity : baseIdentity, result.DeclaringType);
        Assert.Equal(CliValueKind.I4, result.Signature.ReturnType);
    }

    // A reference through the assembly's own type has no original contract to check.
    [Fact]
    public void AReferenceThroughTheAssemblysOwnTypeChecksNoOriginalContract()
    {
        var (provider, token) = OwnTypeReference("Run", [0x00, 0x00, 0x08]);
        using var _ = provider;
        var method = MetadataActorTestData.Method with { Signature = MethodSignatureModel.Create(CliValueKind.I4) };
        var resolver = new MetadataMethodReferenceResolver(new MetadataEntityHandleReader(),
            new FixedSignatureTypeResolver(CliTypeIdentity.FromDefinition(MetadataActorTestData.Type)),
            new SignatureTypeComparer(), new TypeRepository(MetadataActorTestData.Type),
            new FixedTypeDefinitionResolver(MetadataActorTestData.Type), new MethodRepository(method),
            new UnusedRuntimeProvidedMethodResolver(), new IdentityStackTypeResolver(), new UnusedContractValidator(),
            NoRuntimeTypes, UnrelatedContract, new UncheckedPrivateAccess(), new NoBaseTypes());

        Assert.Same(method, resolver.Resolve(OwnSource(provider), token, "Caller.Run", 0).Definition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveArrayAccessUsesTheCallersGenericContext(bool methodParameter)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Arrays.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var parent = metadata.AddTypeReference(default,
            metadata.GetOrAddString("Tests"), metadata.GetOrAddString("Array"));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(isInstanceMethod: true).Parameters(2,
            result =>
            {
                if (methodParameter)
                    result.Type().GenericMethodTypeParameter(0);
                else
                    result.Type().GenericTypeParameter(0);
            },
            parameters =>
            {
                parameters.AddParameter().Type().Int32();
                parameters.AddParameter().Type().Int32();
            });
        var reference = metadata.AddMemberReference(parent,
            metadata.GetOrAddString("Get"), metadata.GetOrAddBlob(signature));
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var source = new MetadataAssemblySnapshot(MetadataActorTestData.Assembly,
            provider.GetMetadataReader(), new Dictionary<int, TypeDefinitionModel>(),
            new Dictionary<int, FieldDefinitionModel>(), new Dictionary<int, MethodDefinitionModel>(),
            new Dictionary<int, EntityHandle>());
        var element = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
        var array = CliTypeIdentity.Array(element, 2);
        var resolver = new MetadataMethodReferenceResolver(
            new MetadataEntityHandleReader(), new FixedSignatureTypeResolver(array),
            new SignatureTypeComparer(), new UnusedTypeRepository(),
            new UnusedTypeDefinitionResolver(), new UnusedMethodRepository(),
            new ArrayMethodResolver(new SignatureTypeComparer()),
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());
        var context = methodParameter ? new CliGenericContext([], [element])
            : new CliGenericContext([element], []);

        var result = resolver.Resolve(source, MetadataTokens.GetToken(reference), "Caller.Run", 0, context);

        Assert.Equal(array, result.DeclaringType);
        Assert.Equal(element, result.Signature.ReturnSignatureType);
        Assert.Equal(2, result.Signature.ParameterSignatureTypes.Length);
    }

    [Theory]
    [InlineData("match")]
    [InlineData("name")]
    [InlineData("signature")]
    [InlineData("static")]
    [InlineData("arity")]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    public void DefinitionTokensBindOnlyMatchingPlatformMethods(string scenario)
    {
        var originalType = MetadataActorTestData.Type with { GenericArity = 1 };
        var original = MetadataActorTestData.Method with
        {
            Key = new(originalType.Key.Assembly, 0x06000001),
            GenericArity = 1,
            Signature = new(CliTypeIdentity.GenericParameter(true, 0),
                [CliTypeIdentity.GenericParameter(false, 0)])
        };
        var platform = new AssemblyIdentity("Platform");
        var targetKey = new EntityKey(platform, 0x02000001);
        var target = original with { Key = new(platform, 0x06000001), DeclaringType = targetKey };
        target = scenario switch
        {
            "name" => target with { Name = "Other" },
            "signature" => target with { Signature = MethodSignatureModel.Create(CliValueKind.Void) },
            "static" => target with { IsStatic = !original.IsStatic },
            "arity" => target with { GenericArity = 2 },
            _ => target
        };
        var alternate = target with { Key = new(platform, 0x06000002) };
        var targetType = originalType with
        {
            Key = targetKey,
            Methods = scenario == "missing" ? [] : scenario == "ambiguous" ? [target.Key, alternate.Key] : [target.Key]
        };
        var source = new MetadataAssemblySnapshot(originalType.Key.Assembly, null!,
            new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel> { [original.Key.MetadataToken] = original },
            new Dictionary<int, EntityHandle>())
        {
            AssemblyIdentityAliases = new AssemblyIdentityAliases(ImmutableDictionary<string, string>.Empty,
                ImmutableDictionary<(AssemblyIdentity, string), AssemblyIdentity>.Empty
                    .Add((originalType.Key.Assembly, originalType.FullName), platform))
        };
        var contracts = new RecordingContractValidator();
        var resolver = new MetadataMethodReferenceResolver(new DefinitionHandleReader(),
            new UnusedSignatureTypeResolver(), new SignatureTypeComparer(), new TypeRepository(originalType),
            new FixedTypeDefinitionResolver(targetType), new MethodRepository(target, alternate),
            new UnusedRuntimeProvidedMethodResolver(), new IdentityStackTypeResolver(), contracts, NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());
        var context = new CliGenericContext([CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
            [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);
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
        Assert.Equal("primitive:i8", Assert.Single(result.MethodArguments).CanonicalName);
        Assert.Equal("primitive:i8", result.Signature.ReturnSignatureType.CanonicalName);
        Assert.Equal("primitive:i4", Assert.Single(result.Signature.ParameterSignatureTypes).CanonicalName);
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
                new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());
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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());
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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());

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
            new IdentityStackTypeResolver(), new UnusedContractValidator(), NoRuntimeTypes,
            ImmutableDictionary<(AssemblyIdentity, string), OriginalContractType>.Empty, new UncheckedPrivateAccess(), new NoBaseTypes());
        var runtimeResolved = ((IMetadataMethodReferenceResolver)runtimeResolver).Resolve(
            source,
            MetadataTokens.GetToken(handle),
            "Test.Method",
            0);

        Assert.Equal(runtimeProvided, runtimeResolved);
    }

    private sealed class UnusedContractValidator : IFrameworkMethodContractValidator
    {
        public void Validate(FrameworkMethodBinding binding) =>
            throw new InvalidOperationException("Unmapped calls must not inspect redirected contracts.");
    }

    private sealed class RecordingContractValidator : IFrameworkMethodContractValidator
    {
        public FrameworkMethodBinding? Binding { get; private set; }
        public void Validate(FrameworkMethodBinding binding) => Binding = binding;
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
