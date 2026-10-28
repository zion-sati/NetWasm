using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests;

public sealed class DispatchActorTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");
    private static readonly EntityKey TypeKey = new(Assembly, 1);
    private static readonly CliTypeIdentity DelegateType = CliTypeIdentity.Named(
        Assembly,
        "Tests",
        "Callback",
        isValueType: false);

    [Fact]
    public void DispatchSiteKeyBuilderUsesStableCanonicalIdentity()
    {
        Assert.Equal(
            "Tests.Callback::Invoke@0000002a",
            ((IDispatchSiteKeyBuilder)new DispatchSiteKeyBuilder()).Build(
                "Tests.Callback::Invoke",
                42));
    }

    [Theory]
    [InlineData(".ctor", 1)]
    [InlineData("Invoke", 2)]
    [InlineData("BeginInvoke", 0)]
    public void DelegateMethodClassifierClassifiesRecognizedDelegateMembers(
        string name,
        int expected)
    {
        Assert.Equal(
            expected,
            (int)((IDelegateMethodClassifier)new DelegateMethodClassifier(
                new FixedDelegateTypeRecognizer(true))).Classify(Method(name)));
    }

    [Fact]
    public void DelegateMethodClassifierRejectsMembersOfOtherTypes()
    {
        Assert.Equal(
            DelegateMethodKind.None,
            ((IDelegateMethodClassifier)new DelegateMethodClassifier(
                new FixedDelegateTypeRecognizer(false))).Classify(Method("Invoke")));
    }

    [Fact]
    public void DelegateTypeRecognizerWalksBaseTypesAndHandlesShapeBoundaries()
    {
        var delegateType = CliTypeIdentity.Named(
            Assembly,
            "System",
            "Delegate",
            false);
        var derivedType = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Callback",
            false);
        var recognizer = new DelegateTypeRecognizer(
            new NamedTypeDefinitionResolver(),
            new FixedBaseTypeResolver(derivedType, delegateType));

        Assert.True(recognizer.Recognize(derivedType));
        Assert.False(recognizer.Recognize(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));

        var unrelated = CliTypeIdentity.Named(Assembly, "Tests", "Value", false);
        Assert.False(new DelegateTypeRecognizer(
            new NamedTypeDefinitionResolver(),
            new FixedBaseTypeResolver(unrelated, null)).Recognize(unrelated));
    }

    [Theory]
    [InlineData("IEnumerable`1", "GetEnumerator", "GetEnumerator")]
    [InlineData("ICollection`1", "get_Count", "GetCount")]
    [InlineData("ICollection`1", "get_IsReadOnly", "GetIsReadOnly")]
    [InlineData("ICollection`1", "Add", "Add")]
    [InlineData("ICollection`1", "Clear", "Clear")]
    [InlineData("ICollection`1", "Contains", "Contains")]
    [InlineData("ICollection`1", "CopyTo", "CopyTo")]
    [InlineData("ICollection`1", "Remove", "Remove")]
    [InlineData("IList`1", "get_Item", "GetItem")]
    [InlineData("IList`1", "set_Item", "SetItem")]
    [InlineData("IList`1", "IndexOf", "IndexOf")]
    [InlineData("IList`1", "Insert", "Insert")]
    [InlineData("IList`1", "RemoveAt", "RemoveAt")]
    [InlineData("IReadOnlyCollection`1", "get_Count", "GetCount")]
    [InlineData("IReadOnlyList`1", "get_Item", "GetItem")]
    [InlineData("IReadOnlyList`1", "Unknown", null)]
    public void DispatchTargetResolverMapsArrayInterfacesToRuntimeHelpers(
        string interfaceName,
        string declarationName,
        string? helperName)
    {
        var arrayElement = CliTypeIdentity.Named(
            Assembly,
            "System",
            "String",
            false);
        var interfaceArgument = CliTypeIdentity.Named(
            Assembly,
            "System",
            "Object",
            false);
        var receiver = CliTypeIdentity.SzArray(arrayElement);
        var interfaceType = CliTypeIdentity.Named(
            Assembly,
            "System.Collections.Generic",
            interfaceName,
            false);
        var requestedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceType,
            [interfaceArgument]);
        var declaration = MethodInstance(
            declarationName,
            30,
            requestedInterface,
            false);
        var helperType = new TypeDefinitionModel(
            new EntityKey(Assembly, 31),
            "System.Collections.Generic",
            "SZArrayHelper",
            false,
            [],
            [new EntityKey(Assembly, 33), new EntityKey(Assembly, 32)]);
        var unrelatedHelperMethod = CreateHelperMethod("Other", 33, helperType.Key);
        var helperMethod = new MethodDefinitionModel(
            new EntityKey(Assembly, 32),
            helperType.Key,
            helperName ?? "OtherArrayHelper",
            false,
            declaration.Signature,
            1)
        {
            GenericArity = 1,
        };
        var relationships = new FixedRelationshipClassifier();
        var resolver = new DispatchTargetResolver(
            new FixedHelperTypeFinder(helperType),
            new FixedTypeDefinitionResolver(requestedInterface, interfaceName),
            new FixedMethodRepository(unrelatedHelperMethod, helperMethod),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(helperMethod, receiver),
            new FixedSymbolFormatter(),
            relationships,
            new NullBaseTypeResolver());

        var originalRelationshipCallCount = relationships.CallCount;

        var target = resolver.Resolve(declaration, receiver);
        var resolvedRelationshipCallCount = relationships.CallCount;

        var cachedTarget = resolver.Resolve(declaration, receiver);


        Assert.Same(target, cachedTarget);

        Assert.True(resolvedRelationshipCallCount > originalRelationshipCallCount);
        Assert.Equal(resolvedRelationshipCallCount, relationships.CallCount);
        Assert.NotNull(target);

        if (helperName is null)
        {
            Assert.Equal(declaration.Definition, target.Method.Definition);
            return;
        }
        Assert.Equal(receiver, target.ReceiverType);
        Assert.Equal(helperMethod, target.Method.Definition);
        Assert.Equal(interfaceArgument, Assert.Single(target.Method.MethodArguments));
    }

    [Fact]
    public void DispatchTargetResolverRejectsAnUnrelatedReceiverAndCachesTheDecision()
    {
        var declarationType = CliTypeIdentity.Named(
            Assembly,
            "Dispatch",
            "Contract",
            false);
        var receiver = CliTypeIdentity.Named(
            Assembly,
            "Dispatch",
            "Unrelated",
            false);
        var declaration = MethodInstance("Invoke", 39, declarationType, true);
        var relationships = new TestTypeRelationshipClassifier(isAssignable: false);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(declarationType, "Contract"),
            new FixedMethodRepository(declaration.Definition),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(declaration.Definition, receiver),
            new FixedSymbolFormatter(),
            relationships,
            new NullBaseTypeResolver());

        Assert.Null(resolver.Resolve(declaration, receiver));
        Assert.Null(resolver.Resolve(declaration, receiver));
        Assert.Equal(1, relationships.CallCount);
    }

    [Fact]
    public void DispatchTargetResolverFindsVirtualOverridesAndComparesParameterSignatures()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Derived", false);
        var declarationType = CliTypeIdentity.Named(Assembly, "Tests", "Base", false);
        var declaration = MethodInstance("Run", 40, declarationType, true);
        var implementation = new MethodDefinitionModel(
            new EntityKey(Assembly, 41),
            new EntityKey(Assembly, 42),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1)
        {
            IsVirtual = true,
            IsNewSlot = false,
        };
        Assert.Empty(declaration.Signature.ParameterSignatureTypes);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ConcreteTypeDefinitionResolver(implementation),
            new FixedMethodRepository(implementation),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(implementation, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(implementation, target.Method.Definition);
    }

    [Theory]
    [InlineData("Other", 0)]
    [InlineData("Run", 1)]
    [InlineData("Other", 1)]
    public void DispatchTargetResolverInstantiatesOnlyMatchingNameAndArity(
        string unrelatedName,
        int unrelatedArity)
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Derived", false);
        var declarationType = CliTypeIdentity.Named(Assembly, "Tests", "Base", false);
        var declaration = MethodInstance("Run", 40, declarationType, true);
        var implementation = declaration.Definition with
        {
            Key = new EntityKey(Assembly, 41),
            IsNewSlot = false,
        };
        var unrelated = implementation with
        {
            Key = new EntityKey(Assembly, 42),
            Name = unrelatedName,
            GenericArity = unrelatedArity,
        };
        var instances = new RecordingMethodInstanceResolver(receiver, unrelated, implementation);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ConcreteTypeDefinitionResolver(unrelated, implementation),
            new FixedMethodRepository(unrelated, implementation),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            instances,
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);

        Assert.NotNull(target);
        Assert.Equal(implementation, target.Method.Definition);
        Assert.Equal(receiver, target.ReceiverType);
        Assert.Same(target, resolver.Resolve(declaration, receiver));
        Assert.Equal([implementation.Key.MetadataToken], instances.ResolvedTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DispatchTargetResolverUsesExplicitInterfaceImplementations(
        bool constructedReceiver)
    {
        var interfaceType = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "IExplicit`1",
            false);
        var receiverDefinition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Explicit`1",
            false);
        var argument = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var receiver = constructedReceiver
            ? CliTypeIdentity.GenericInstantiation(receiverDefinition, [argument])
            : receiverDefinition;
        var declaration = MethodInstance("Read", 34, interfaceType, true);
        var implementationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 35),
            new EntityKey(Assembly, 36),
            "Tests.IExplicit.Read",
            false,
            declaration.Signature,
            1);
        var implementation = new MethodInstanceModel(
            implementationDefinition,
            receiver,
            [],
            declaration.Signature);
        var mapping = new MethodImplementationInstanceModel(
            implementation,
            declaration);
        var wrongKey = new MethodImplementationInstanceModel(
            implementation,
            MethodInstance("Read", 134, interfaceType, true));
        var wrongDeclaringType = new MethodImplementationInstanceModel(
            implementation,
            declaration with { DeclaringType = receiverDefinition });
        var wrongSignature = new MethodImplementationInstanceModel(
            implementation,
            declaration with
            {
                Signature = MethodSignatureModel.Create(CliValueKind.I4),
            });
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ExplicitTypeDefinitionResolver(interfaceType),
            new FixedMethodRepository(implementationDefinition),
            new FixedMethodImplementationResolver(
                wrongKey,
                wrongDeclaringType,
                wrongSignature,
                mapping),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(implementationDefinition, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(implementationDefinition, target.Method.Definition);
    }

    [Fact]
    public void DispatchTargetResolverUsesVariantExplicitInterfaceMappings()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Receiver", false);
        var interfaceDefinition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "IOutput`1",
            false);
        var stringType = CliTypeIdentity.Named(Assembly, "System", "String", false);
        var objectType = CliTypeIdentity.Named(Assembly, "System", "Object", false);
        var implementedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [stringType]);
        var requestedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [objectType]);
        var declaration = MethodInstance("Read", 37, requestedInterface, true);
        var mappedDeclaration = declaration with
        {
            DeclaringType = implementedInterface,
        };
        var implementationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 38),
            new EntityKey(Assembly, 39),
            "Read",
            false,
            declaration.Signature,
            1);
        var implementation = new MethodInstanceModel(
            implementationDefinition,
            receiver,
            [],
            declaration.Signature);
        var decoyDeclaration = MethodInstance(
            "Read",
            137,
            implementedInterface,
            true);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ExplicitTypeDefinitionResolver(requestedInterface),
            new FixedMethodRepository(implementationDefinition),
            new FixedMethodImplementationResolver(
                new MethodImplementationInstanceModel(
                    implementation,
                    decoyDeclaration),
                new MethodImplementationInstanceModel(
                    implementation,
                    mappedDeclaration)),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(implementationDefinition, receiver),
            new FixedSymbolFormatter(),
            new VariantRelationshipClassifier(
                receiver,
                implementedInterface,
                requestedInterface,
                stringType,
                objectType),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(implementationDefinition, target.Method.Definition);
    }

    [Fact]
    public void DispatchTargetResolverUsesConstructedDeclaringTypeImplementation()
    {
        var definition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Container`1",
            false);
        var receiver = CliTypeIdentity.GenericInstantiation(
            definition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var declaration = MethodInstance("Run", 140, receiver, true);
        var implementation = new MethodDefinitionModel(
            new EntityKey(Assembly, 141),
            new EntityKey(Assembly, 142),
            "Run",
            true,
            declaration.Signature,
            1)
        {
            IsVirtual = true,
            IsNewSlot = true,
        };
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ConcreteTypeDefinitionResolver(implementation),
            new FixedMethodRepository(implementation),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(implementation, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(implementation, target.Method.Definition);
    }

    [Fact]
    public void DispatchTargetResolverFindsImplicitCovariantInterfaceImplementation()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Iterator", true);
        var interfaceDefinition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "IOutput`1",
            false);
        var stringType = CliTypeIdentity.Named(Assembly, "System", "String", false);
        var objectType = CliTypeIdentity.Named(Assembly, "System", "Object", false);
        var implementedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [stringType]);
        var requestedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [objectType]);
        var declarationSignature = MethodSignatureModel.Create(objectType);
        var declarationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000040),
            new EntityKey(Assembly, 0x02000040),
            "Read",
            false,
            declarationSignature,
            0)
        {
            IsVirtual = true,
            IsAbstract = true,
        };
        var declaration = new MethodInstanceModel(
            declarationDefinition,
            requestedInterface,
            [],
            declarationSignature);
        var implementationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000041),
            TypeKey,
            declaration.Definition.Name,
            false,
            MethodSignatureModel.Create(stringType),
            1);
        var relationships = new VariantRelationshipClassifier(
            receiver,
            implementedInterface,
            requestedInterface,
            stringType,
            objectType);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new VariantTypeDefinitionResolver(
                receiver,
                requestedInterface,
                implementationDefinition),
            new FixedMethodRepository(implementationDefinition),
            new EmptyMethodImplementationResolver(),
            new FixedImplementedInterfaceResolver(implementedInterface),
            new FixedMethodInstanceResolver(implementationDefinition, receiver),
            new FixedSymbolFormatter(),
            relationships,
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(implementationDefinition, target.Method.Definition);
    }

    [Fact]
    public void DispatchTargetResolverFindsImplicitContravariantInterfaceImplementation()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Consumer", false);
        var interfaceDefinition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "IInput`1",
            false);
        var stringType = CliTypeIdentity.Named(Assembly, "System", "String", false);
        var objectType = CliTypeIdentity.Named(Assembly, "System", "Object", false);
        var implementedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [objectType]);
        var requestedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [stringType]);
        var declarationSignature = MethodSignatureModel.Create(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            stringType);
        var declarationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000042),
            new EntityKey(Assembly, 0x02000042),
            "Write",
            false,
            declarationSignature,
            0)
        {
            IsVirtual = true,
            IsAbstract = true,
        };
        var declaration = new MethodInstanceModel(
            declarationDefinition,
            requestedInterface,
            [],
            declarationSignature);
        var implementationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000043),
            TypeKey,
            "Write",
            false,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                objectType),
            1);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new VariantTypeDefinitionResolver(
                receiver,
                requestedInterface,
                implementationDefinition),
            new FixedMethodRepository(implementationDefinition),
            new EmptyMethodImplementationResolver(),
            new FixedImplementedInterfaceResolver(implementedInterface),
            new FixedMethodInstanceResolver(implementationDefinition, receiver),
            new FixedSymbolFormatter(),
            new VariantRelationshipClassifier(
                receiver,
                implementedInterface,
                requestedInterface,
                stringType,
                objectType),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(implementationDefinition, target.Method.Definition);
    }

    [Fact]
    public void DispatchTargetResolverRejectsVariantParameterCountMismatch()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Consumer", false);
        var interfaceDefinition = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "IInput`1",
            false);
        var stringType = CliTypeIdentity.Named(Assembly, "System", "String", false);
        var objectType = CliTypeIdentity.Named(Assembly, "System", "Object", false);
        var implementedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [objectType]);
        var requestedInterface = CliTypeIdentity.GenericInstantiation(
            interfaceDefinition,
            [stringType]);
        var declarationSignature = MethodSignatureModel.Create(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            stringType);
        var declarationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000044),
            new EntityKey(Assembly, 0x02000044),
            "Write",
            false,
            declarationSignature,
            0)
        {
            IsVirtual = true,
            IsAbstract = true,
        };
        var declaration = new MethodInstanceModel(
            declarationDefinition,
            requestedInterface,
            [],
            declarationSignature);
        var implementationDefinition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000045),
            TypeKey,
            "Write",
            false,
            MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                objectType,
                objectType),
            1);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new VariantTypeDefinitionResolver(
                receiver,
                requestedInterface,
                implementationDefinition),
            new FixedMethodRepository(implementationDefinition),
            new EmptyMethodImplementationResolver(),
            new FixedImplementedInterfaceResolver(implementedInterface),
            new FixedMethodInstanceResolver(implementationDefinition, receiver),
            new FixedSymbolFormatter(),
            new VariantRelationshipClassifier(
                receiver,
                implementedInterface,
                requestedInterface,
                stringType,
                objectType),
            new NullBaseTypeResolver());

        Assert.Null(resolver.Resolve(declaration, receiver));
    }

    [Fact]
    public void DispatchTargetResolverUsesConcreteDefaultInterfaceMethod()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Receiver", false);
        var interfaceType = CliTypeIdentity.Named(Assembly, "Tests", "IDefault", false);
        var declaration = MethodInstance("Read", 70, interfaceType, true);
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(interfaceType),
            new FixedMethodRepository(),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(declaration.Definition, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        var target = resolver.Resolve(declaration, receiver);
        Assert.NotNull(target);

        Assert.Equal(declaration, target.Method);
    }

    [Fact]
    public void DispatchTargetResolverSkipsArrayDuringOrdinaryInterfaceLookup()
    {
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var receiver = CliTypeIdentity.Array(element, 2);
        var interfaceType = CliTypeIdentity.Named(Assembly, "Tests", "IContract", false);
        var originalDeclaration = MethodInstance("Read", 72, interfaceType, true);
        var declaration = originalDeclaration with
        {
            Definition = originalDeclaration.Definition with { IsAbstract = true },
        };
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new FixedTypeDefinitionResolver(interfaceType),
            new FixedMethodRepository(),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(declaration.Definition, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        Assert.Null(resolver.Resolve(declaration, receiver));
    }

    [Fact]
    public void DispatchTargetResolverRejectsParameterSignatureMismatches()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Derived", false);
        var declarationType = CliTypeIdentity.Named(Assembly, "Tests", "Base", false);
        var declaration = MethodInstance("Run", 42, declarationType, true);
        var mismatched = new MethodDefinitionModel(
            new EntityKey(Assembly, 43),
            new EntityKey(Assembly, 44),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I4),
            1)
        {
            IsVirtual = true,
            IsNewSlot = false,
        };
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ConcreteTypeDefinitionResolver(mismatched),
            new FixedMethodRepository(mismatched),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(mismatched, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        Assert.Null(resolver.Resolve(declaration, receiver));
    }

    [Fact]
    public void DispatchTargetResolverRejectsReturnSignatureMismatches()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Derived", false);
        var declarationType = CliTypeIdentity.Named(Assembly, "Tests", "Base", false);
        var declaration = MethodInstance("Run", 45, declarationType, true);
        var mismatchedReturn = new MethodDefinitionModel(
            new EntityKey(Assembly, 46),
            new EntityKey(Assembly, 47),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            1)
        {
            IsVirtual = true,
            IsNewSlot = false,
        };
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ConcreteTypeDefinitionResolver(mismatchedReturn),
            new FixedMethodRepository(mismatchedReturn),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(mismatchedReturn, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        Assert.Null(resolver.Resolve(declaration, receiver));
    }

    [Fact]
    public void DispatchTargetResolverSkipsNonOverrideImplementations()
    {
        var receiver = CliTypeIdentity.Named(Assembly, "Tests", "Derived", false);
        var declarationType = CliTypeIdentity.Named(Assembly, "Tests", "Base", false);
        var declaration = MethodInstance("Run", 50, declarationType, true);
        var implementation = new MethodDefinitionModel(
            new EntityKey(Assembly, 51),
            new EntityKey(Assembly, 52),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1)
        {
            IsVirtual = false,
            IsNewSlot = true,
        };
        var resolver = new DispatchTargetResolver(
            new UnusedTypeFinder(),
            new ConcreteTypeDefinitionResolver(implementation),
            new FixedMethodRepository(implementation),
            new EmptyMethodImplementationResolver(),
            new EmptyImplementedInterfaceResolver(),
            new FixedMethodInstanceResolver(implementation, receiver),
            new FixedSymbolFormatter(),
            new NonArrayRelationshipClassifier(),
            new NullBaseTypeResolver());

        Assert.Null(resolver.Resolve(declaration, receiver));
    }

    private static MethodInstanceModel MethodInstance(
        string name,
        int token,
        CliTypeIdentity declaringType,
        bool isVirtual) =>
        new(
            new MethodDefinitionModel(
                new EntityKey(Assembly, token),
                new EntityKey(Assembly, token + 100),
                name,
                false,
                MethodSignatureModel.Create(CliValueKind.Void),
                1)
            {
                IsVirtual = isVirtual,
            },
            declaringType,
            [],
            MethodSignatureModel.Create(CliValueKind.Void));

    private static MethodInstanceModel Method(string name)
    {
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 2),
            TypeKey,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        return new MethodInstanceModel(
            definition,
            DelegateType,
            [],
            definition.Signature);
    }

    private static MethodDefinitionModel CreateHelperMethod(
        string name,
        int token,
        EntityKey declaringType) => new(
            new EntityKey(Assembly, token),
            declaringType,
            name,
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1)
        {
            GenericArity = 1,
        };

    private sealed class FixedDelegateTypeRecognizer(bool result) :
        IDelegateTypeRecognizer
    {
        public bool Recognize(CliTypeIdentity type) => result;
    }

    private sealed class NamedTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            var fullName = identity.FullName!;
            var separator = fullName.LastIndexOf('.');
            return new(
                new EntityKey(Assembly, fullName == "System.Delegate" ? 10 : 11),
                separator < 0 ? "" : fullName[..separator],
                separator < 0 ? fullName : fullName[(separator + 1)..],
                false,
                [],
                []);
        }
    }

    private sealed class FixedBaseTypeResolver(
        CliTypeIdentity source,
        CliTypeIdentity? baseType) : IBaseTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) =>
            type == source ? baseType : null;
    }

    private sealed class FixedHelperTypeFinder(TypeDefinitionModel helper) : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => helper;
    }

    private sealed class UnusedTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) =>
            throw new InvalidOperationException();
    }

    private sealed class FixedTypeDefinitionResolver(
        CliTypeIdentity interfaceType,
        string interfaceName = "IEnumerable`1") : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            new EntityKey(Assembly, 40),
            "System.Collections.Generic",
            interfaceName,
            false,
            [],
            [])
        {
            IsInterface = identity == interfaceType,
        };
    }

    private sealed class FixedMethodRepository(params MethodDefinitionModel[] methods) : IMethodRepository
    {
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods =
            methods.ToDictionary(method => method.Key);

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];
    }

    private sealed class EmptyMethodImplementationResolver : IMethodImplementationResolver
    {
        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
            CliTypeIdentity type) => [];
    }

    private sealed class FixedMethodImplementationResolver(
        params MethodImplementationInstanceModel[] mappings) :
        IMethodImplementationResolver
    {
        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(
            CliTypeIdentity type) => [.. mappings];
    }

    private sealed class EmptyImplementedInterfaceResolver : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) => [];
    }

    private sealed class FixedImplementedInterfaceResolver(
        CliTypeIdentity implemented) : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) =>
            [implemented];
    }

    private sealed class VariantTypeDefinitionResolver(
        CliTypeIdentity receiver,
        CliTypeIdentity interfaceType,
        MethodDefinitionModel implementation) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            identity == receiver ? TypeKey : new EntityKey(Assembly, 0x02000040),
            "Tests",
            identity == receiver ? "Iterator" : "IOutput`1",
            identity == receiver,
            [],
            identity == receiver ? [implementation.Key] : [])
        {
            IsInterface = identity == interfaceType,
        };
    }

    private sealed class VariantRelationshipClassifier(
        CliTypeIdentity receiver,
        CliTypeIdentity implementedInterface,
        CliTypeIdentity requestedInterface,
        CliTypeIdentity implementationReturn,
        CliTypeIdentity declarationReturn) : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) => new((candidate == target ||
                                candidate == receiver && target == requestedInterface ||
                                candidate == implementedInterface && target == requestedInterface
                    ? TypeRelationshipCharacteristics.Hierarchy
                    : TypeRelationshipCharacteristics.None) |
                    (candidate == implementedInterface && target == requestedInterface ||
                     candidate == implementationReturn && target == declarationReturn
                    ? TypeRelationshipCharacteristics.GenericVariance
                    : TypeRelationshipCharacteristics.None));
    }

    private sealed class FixedMethodInstanceResolver(
        MethodDefinitionModel method,
        CliTypeIdentity declaringType) : IMethodInstanceResolver
    {
        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null) =>
            new(method, declaringType, genericContext?.MethodArguments ?? [], method.Signature);
    }

    private sealed class RecordingMethodInstanceResolver(
        CliTypeIdentity declaringType,
        params MethodDefinitionModel[] definitions) : IMethodInstanceResolver
    {
        public List<int> ResolvedTokens { get; } = [];

        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null)
        {
            ResolvedTokens.Add(metadataToken);
            var definition = definitions.Single(method => method.Key.MetadataToken == metadataToken);
            return new(definition, declaringType, genericContext?.MethodArguments ?? [], definition.Signature);
        }
    }

    private sealed class FixedSymbolFormatter : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    private sealed class FixedRelationshipClassifier : ITypeRelationshipClassifier
    {
        public int CallCount { get; private set; }


        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target)

        {

            CallCount++;

            return new(TypeRelationshipCharacteristics.SzArrayGenericInterface |
                    TypeRelationshipCharacteristics.Hierarchy);

        }
    }

    private sealed class NullBaseTypeResolver : IBaseTypeResolver
    {
        public CliTypeIdentity? Resolve(CliTypeIdentity type) => null;
    }

    private sealed class ConcreteTypeDefinitionResolver(params MethodDefinitionModel[] methods) :
        ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            new EntityKey(Assembly, 45),
            "Tests",
            "Derived",
            false,
            [],
            [.. methods.Select(method => method.Key)]);
    }

    private sealed class ExplicitTypeDefinitionResolver(
        CliTypeIdentity interfaceType) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            new EntityKey(Assembly, identity == interfaceType ? 46 : 47),
            "Tests",
            identity == interfaceType ? "IExplicit`1" : "Explicit`1",
            false,
            [],
            [])
        {
            IsInterface = identity == interfaceType,
        };
    }

    private sealed class NonArrayRelationshipClassifier : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) => new(TypeRelationshipCharacteristics.Hierarchy);
    }
}
