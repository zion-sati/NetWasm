using System.Collections.Immutable;
using System.Globalization;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedLayoutPhaseTests
{
    [Fact]
    public void ValueTypeLayoutResolverDoesNotMaterializeOpenGenericRuntimeDescriptors()
    {
        var key = new EntityKey(new("LayoutPolicyTests"), 52);
        var definition = Definition(key, "Tests.Cell`1", true) with { GenericArity = 1 };
        var identity = CliTypeIdentity.FromDefinition(definition);
        var values = new RecordingValueLayoutResolver();
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var descriptor = new ObjectLayout(7, 8, []);
        state.Objects.Add(key, descriptor);
        var resolver = new ValueTypeLayoutResolver(new TypeRepository([definition]),
            new FieldRepository([]), new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
            {
                [key] = identity,
            }), values, state, [definition]);

        ResolveValueTypes(resolver);

        Assert.Empty(values.Types);
        Assert.Empty(values.Definitions);
        Assert.Equal(descriptor, state.Objects[key]);
    }

    [Fact]
    public void ValueTypeLayoutResolverSkipsNonMaterializableCanonicalTypes()
    {
        var assembly = new AssemblyIdentity("LayoutPolicyTests");
        var voidKey = new EntityKey(assembly, 50);
        var valueKey = new EntityKey(assembly, 51);
        var voidDefinition = Definition(voidKey, "System.Void", true);
        var valueDefinition = Definition(valueKey, "System.Int32", true);

        var referenceKey = new EntityKey(assembly, 3);
        var referenceDefinition = valueDefinition with
        {
            Key = referenceKey,
            Name = "Reference",
            IsValueType = false,
        };
        var voidIdentity = CliTypeIdentity.FromDefinition(voidDefinition);
        var valueIdentity = CliTypeIdentity.FromDefinition(valueDefinition);
        var identities = new MappingIdentities(
            new[] { voidDefinition, valueDefinition, referenceDefinition }.ToDictionary(
                static definition => definition.Key,
                CliTypeIdentity.FromDefinition));
        var valueLayouts = new RecordingValueLayoutResolver();
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        state.Objects.Add(voidKey, new ObjectLayout(0, 0, []));
        state.Objects.Add(referenceKey, new ObjectLayout(0, 0, []));
        var resolver = new ValueTypeLayoutResolver(
            new TypeRepository([voidDefinition, valueDefinition, referenceDefinition]),
            new FieldRepository([]),
            identities,
            valueLayouts,
            state,
            [voidDefinition, valueDefinition, referenceDefinition]);

        resolver.Resolve();

        Assert.DoesNotContain(valueLayouts.Types, identity => !identity.IsValueType);
        var resolved = Assert.Single(valueLayouts.Types);
        Assert.Same(valueDefinition, Assert.Single(valueLayouts.Definitions));
        Assert.Equal(valueIdentity, resolved);
        Assert.True(resolved.HasRuntimeStorage);
        Assert.DoesNotContain(valueLayouts.Types, static identity => !identity.HasRuntimeStorage);
    }

    [Fact]
    public void LayoutPoliciesResolveTypeIdentitiesAndExceptionNamesThroughContracts()
    {
        var assembly = new AssemblyIdentity("LayoutPolicyTests");
        var intKey = new EntityKey(assembly, 1);
        var typeIdentities = As<ITypeIdentityResolver>(
            new MappingIdentities(
            new Dictionary<EntityKey, CliTypeIdentity>
            {
                [intKey] = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            }));
        var exceptionNames = As<IExceptionTypeNameResolver>(new ExceptionTypeNameResolver());

        Assert.Equal(
            "primitive:i4",
            ResolveTypeIdentity(typeIdentities, intKey).CanonicalName);
        Assert.Equal(
            "System.ArgumentOutOfRangeException",
            ResolveExceptionName(exceptionNames, ManagedExceptionKind.ArgumentOutOfRange));
    }

    [Fact]
    public void BitmapBuilderWritesTargetSizedReferenceWordsThroughItsContract()
    {
        var builder = As<IStaticReferenceBitmapBuilder>(new StaticReferenceBitmapBuilder());

        var wasm32 = BuildBitmap(builder, [0, 4], 2, sizeof(uint));
        var wasm64 = BuildBitmap(builder, [0, 8], 2, sizeof(ulong));

        Assert.Equal(0b11u, BitConverter.ToUInt32(wasm32));
        Assert.Equal(0b11UL, BitConverter.ToUInt64(wasm64));
    }

    [Fact]
    public void StaticFieldStoragePublishesRvaDataAndRejectsOversizedInitializers()
    {
        var assembly = new AssemblyIdentity("StaticFieldDataTests");
        var typeKey = new EntityKey(assembly, 1);
        var fieldKey = new EntityKey(assembly, 2);
        var fieldType = CliTypeIdentity.Named(assembly, "Tests", "Blob", true);
        var field = new FieldDefinitionModel(
            fieldKey,
            typeKey,
            "Data",
            fieldType,
            true)
        {
            InitialData = [0xc0, 0xaf],
        };
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var layouts = EmptyLayouts(target);
        layouts.ValueLayoutState.Values.Add(fieldType, new(fieldType, 2, 1, []));
        var state = new ManagedStaticDataBuildState();
        BuildStaticFields(As<IStaticFieldStorageBuilder>(new StaticFieldStorageBuilder(
            new TypeRepository([Definition(typeKey, "Tests.Container", false)]),
            new FieldRepository([field]),
            EmptyProgram() with { Fields = [fieldKey] },
            layouts,
            state)));

        var segment = Assert.Single(state.Segments);
        Assert.Equal(state.StaticFields[fieldKey].Address, segment.Address);
        Assert.Equal(field.InitialData, segment.Data);

        var invalidField = field with { InitialData = [0, 1, 2] };
        var error = Assert.Throws<CompilerException>(() => BuildStaticFields(
            As<IStaticFieldStorageBuilder>(new StaticFieldStorageBuilder(
                new TypeRepository([Definition(typeKey, "Tests.Container", false)]),
                new FieldRepository([invalidField]),
                EmptyProgram() with { Fields = [fieldKey] },
                layouts,
                new ManagedStaticDataBuildState()))));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 1, 4, 8, 24)]
    [InlineData(WasmTarget.Wasm64, 1, 4, 8, 24)]
    [InlineData(WasmTarget.Wasm32, 1, 8, 4, 24)]
    [InlineData(WasmTarget.Wasm64, 1, 8, 4, 24)]
    [InlineData(WasmTarget.Wasm32, 16, 4, 8, 32)]
    [InlineData(WasmTarget.Wasm64, 16, 4, 8, 32)]
    [InlineData(WasmTarget.Wasm32, 1, 4, 8, 24, true)]
    [InlineData(WasmTarget.Wasm64, 1, 4, 8, 24, true)]
    public void StaticFieldStorageHonorsMaximumSpanAlignmentAcrossMethodKinds(
        WasmTarget target,
        int storageAlignment,
        int ordinaryAlignment,
        int constructedAlignment,
        int expectedAddress,
        bool genericOwner = false)
    {
        var assembly = new AssemblyIdentity("RvaAlignmentTests");
        var owner = Definition(new EntityKey(assembly, 1), "Tests.Container", false) with
        {
            GenericArity = genericOwner ? 1 : 0,
        };
        var blob = CliTypeIdentity.Named(assembly, "Tests", "Blob", true);
        var field = new FieldDefinitionModel(
            new EntityKey(assembly, 2), owner.Key, "Data", blob, true)
        {
            InitialData = [1, 2, 3, 4, 5, 6, 7, 8],
        };
        var fieldInstance = new FieldInstanceModel(
            field, CliTypeIdentity.FromDefinition(owner), blob);
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var i8 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
        var firstInstance = genericOwner
            ? fieldInstance with
            {
                DeclaringType = CliTypeIdentity.GenericInstantiation(fieldInstance.DeclaringType, [i4]),
            }
            : fieldInstance;
        var secondInstance = genericOwner
            ? fieldInstance with
            {
                DeclaringType = CliTypeIdentity.GenericInstantiation(fieldInstance.DeclaringType, [i8]),
            }
            : fieldInstance;
        var layouts = EmptyLayouts(WasmTargetLayout.For(target));
        layouts.ValueLayoutState.Values.Add(blob, new(blob, 8, storageAlignment, []));
        layouts.ValueLayoutState.Values.Add(i4, new(i4, 4, 4, []));
        layouts.ValueLayoutState.Values.Add(i8, new(i8, 8, 8, []));
        var ordinary = MethodWithLocalValue(Creation(
            ordinaryAlignment,
            firstInstance,
            loadElement: false));
        var constructed = MethodWithLocalValue(Creation(
            constructedAlignment,
            secondInstance,
            loadElement: true));
        var program = EmptyProgram() with
        {
            Fields = [field.Key],
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty
                .Add(ordinary.Method.Definition.Key, ordinary),
            ConstructedMethods = ImmutableDictionary<string, ManagedMethodBody>.Empty
                .Add(constructed.Method.CanonicalName, constructed),
            ConstructedFields = genericOwner
                ? ImmutableDictionary<string, FieldInstanceModel>.Empty
                    .Add(firstInstance.CanonicalName, firstInstance)
                    .Add(secondInstance.CanonicalName, secondInstance)
                : ImmutableDictionary<string, FieldInstanceModel>.Empty,
        };
        var state = new ManagedStaticDataBuildState { Cursor = 17 };

        BuildStaticFields(As<IStaticFieldStorageBuilder>(new StaticFieldStorageBuilder(
            new TypeRepository([owner]), new FieldRepository([field]),
            program, layouts, state)));

        Assert.Equal(expectedAddress, state.StaticFields[field.Key].Address);
        var segment = Assert.Single(state.Segments);
        Assert.Equal(expectedAddress, segment.Address);
        Assert.Equal(field.InitialData, segment.Data);
        Assert.Equal(expectedAddress + 8, state.Cursor);
        Assert.Empty(state.StaticRoots);
        Assert.Equal(genericOwner ? 2 : 0, state.ConstructedStaticFields.Count);
        Assert.All(state.ConstructedStaticFields.Values,
            layout => Assert.Equal(expectedAddress, layout.Address));

        CilInstruction Creation(
            int alignment,
            FieldInstanceModel source,
            bool loadElement)
        {
            var element = alignment == 4 ? i4 : i8;
            var span = CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(assembly, "System", "ReadOnlySpan`1", true),
                [element]);
            var operation = loadElement
                ? CilOperation.LoadStaticDataElement
                : CilOperation.CreateReadOnlySpanFromRva;
            return new(0, 1, operation,
                new CilOperand.ReadOnlySpanData(source, span, element, 8 / alignment));
        }
    }

    [Theory]
    [InlineData(CilOperation.CreateReadOnlySpanFromRva)]
    [InlineData(CilOperation.LoadStaticDataElement)]
    public void StaticFieldStorageRejectsMissingSpanMetadataBeforePlacement(
        CilOperation operation)
    {
        var method = MethodWithLocalValue(new CilInstruction(
            0, 1, operation, new CilOperand.None()));
        var program = EmptyProgram() with
        {
            Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty
                .Add(method.Method.Definition.Key, method),
        };
        var state = new ManagedStaticDataBuildState { Cursor = 17 };

        var error = Assert.Throws<CompilerException>(() => BuildStaticFields(
            As<IStaticFieldStorageBuilder>(new StaticFieldStorageBuilder(
                new TypeRepository([]), new FieldRepository([]), program,
                EmptyLayouts(WasmTargetLayout.Wasm32), state))));

        Assert.Equal(DiagnosticCode.RuntimeContract, error.Diagnostic.Code);
        Assert.Equal(17, state.Cursor);
        Assert.Empty(state.StaticFields);
        Assert.Empty(state.ConstructedStaticFields);
        Assert.Empty(state.Segments);
    }

    [Fact]
    public void TypeCompilerPhasesBuildThroughNarrowInMemoryContracts()
    {
        var assembly = new AssemblyIdentity("CompilerPhaseTests");
        var typeKey = new EntityKey(assembly, 1);
        var type = Definition(typeKey, "Tests.Node", false);
        var templateKey = new EntityKey(assembly, 6);
        var template = Definition(templateKey, "Tests.Template`1", false) with
        {
            GenericArity = 1,
        };
        var objectLayouts = new RecordingObjectLayoutBuilder();
        BuildReachableObjects(As<IReachableObjectLayoutBuilder>(
            new ReachableObjectLayoutBuilder(
                new TypeRepository([type, template]),
                EmptyProgram() with
                {
                    Types = [typeKey, templateKey],
                    RuntimeTypeDefinitions = [templateKey],
                },
                objectLayouts)));
        Assert.Contains(typeKey, objectLayouts.EntityTypes);
        Assert.Contains(templateKey, objectLayouts.EntityTypes);

        var fieldKey = new EntityKey(assembly, 2);
        var field = new FieldDefinitionModel(
            fieldKey,
            typeKey,
            "Value",
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            true);
        var staticValues = new RecordingValueLayoutResolver();
        ResolveStaticFieldValues(As<IStaticFieldValueLayoutResolver>(
            new StaticFieldValueLayoutResolver(
                new FieldRepository([field]),
                EmptyProgram() with { Fields = [fieldKey] },
                staticValues)));
        Assert.Contains("primitive:i4", staticValues.Types.Select(type => type.CanonicalName));

        var method = MethodWithLocalValue();
        var methodValues = new RecordingValueLayoutResolver();
        ResolveMethodValues(As<IMethodValueLayoutResolver>(
            new MethodValueLayoutResolver(
                EmptyProgram() with
                {
                    Methods = ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(
                        method.Method.Definition.Key,
                        method),
                },
                methodValues)));
        Assert.Contains("primitive:i4", methodValues.Types.Select(type => type.CanonicalName));

        var stringDefinition = Definition(
            new EntityKey(assembly, 3),
            "System.String",
            false);
        var exceptionDefinition = Definition(
            new EntityKey(assembly, 4),
            "System.NullReferenceException",
            false);
        var implicitObjects = new RecordingObjectLayoutBuilder();
        BuildImplicitObjects(As<IImplicitObjectLayoutBuilder>(
            new ImplicitObjectLayoutBuilder(
                new TypeFinder([stringDefinition, exceptionDefinition]),
                new TypeRepository([stringDefinition, exceptionDefinition]),
                EmptyProgram() with
                {
                    StringLiterals = ["text"],
                    ImplicitExceptions = [ManagedExceptionKind.NullReference],
                },
                implicitObjects,
                new ExceptionTypeNameResolver(),
                new ManagedTypeLayoutBuildState(new ManagedValueLayoutState()))));
        Assert.Contains(stringDefinition.Key, implicitObjects.EntityTypes);
        Assert.Contains(exceptionDefinition.Key, implicitObjects.EntityTypes);

        var valueKey = new EntityKey(assembly, 5);
        var valueDefinition = Definition(valueKey, "System.Int32", true);
        var valueState = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        valueState.Objects.Add(valueKey, new ObjectLayout(1, 8, []));
        var valueValues = new RecordingValueLayoutResolver();
        ResolveValueTypes(As<IValueTypeLayoutResolver>(
            new ValueTypeLayoutResolver(new TypeRepository([valueDefinition]), new FieldRepository([]), new MappingIdentities(
                new Dictionary<EntityKey, CliTypeIdentity>
                {
                    [valueKey] = CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                }), valueValues, valueState, [new TypeDefinitionModel(
                    valueKey,
                    "System",
                    "Int32",
                    true,
                    [],
                    [])
                {
                    GenericArity = 1,
                }])));
        Assert.Contains("primitive:i4", valueValues.Types.Select(type => type.CanonicalName));
        var predicateTypeKey = new EntityKey(assembly, 6);
        var predicateFieldKey = new EntityKey(assembly, 7);
        var staticPredicateTypeKey = new EntityKey(assembly, 8);
        var staticPredicateFieldKey = new EntityKey(assembly, 9);
        var invalidPredicateTypeKey = new EntityKey(assembly, 14);
        var predicateType = new TypeDefinitionModel(
            predicateTypeKey,
            "Tests",
            "DeclaredValue",
            true,
            [predicateFieldKey],
            [])
        {
            DeclaredSize = 1,
        };
        var staticPredicateType = new TypeDefinitionModel(
            staticPredicateTypeKey,
            "Tests",
            "StaticDeclaredValue",
            true,
            [staticPredicateFieldKey],
            [])
        {
            DeclaredSize = 1,
        };
        var invalidPredicateType = new TypeDefinitionModel(
            invalidPredicateTypeKey,
            "Tests",
            "InvalidDeclaredValue",
            true,
            [],
            [])
        {
            DeclaredSize = 2,
        };
        var predicateIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "DeclaredValue", true);
        var staticPredicateIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "StaticDeclaredValue", true);
        ResolveValueTypes(As<IValueTypeLayoutResolver>(
            new ValueTypeLayoutResolver(new TypeRepository([]), new FieldRepository([
                    new FieldDefinitionModel(
                        predicateFieldKey,
                        predicateTypeKey,
                        "Value",
                        CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                        false),
                    new FieldDefinitionModel(
                        staticPredicateFieldKey,
                        staticPredicateTypeKey,
                        "Value",
                        CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                        true),
                ]), new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
                {
                    [predicateTypeKey] = predicateIdentity,
                    [staticPredicateTypeKey] = staticPredicateIdentity,
                }), new RecordingValueLayoutResolver(), new ManagedTypeLayoutBuildState(new ManagedValueLayoutState()), [predicateType, staticPredicateType, invalidPredicateType])));

        var delegateFields = ImmutableArray.Create(
            "Target",
            "MethodId",
            "InvocationList",
            "InvocationCount")
            .Select((name, index) => new FieldDefinitionModel(
                new EntityKey(assembly, 10 + index),
                typeKey,
                name,
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                false))
            .ToArray();
        var delegateDefinition = new TypeDefinitionModel(
            new EntityKey(assembly, 9),
            "System",
            "Delegate",
            false,
            [.. delegateFields.Select(member => member.Key)],
            []);
        var delegateState = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        foreach (var (member, offset) in delegateFields.Select((member, index) =>
                     (member, index * 4)))
        {
            delegateState.ValueLayouts.Fields.Add(member.Key, new FieldLayout(offset));
        }
        var offsets = ResolveDelegateOffsets(As<IDelegateFieldOffsetResolver>(
            new DelegateFieldOffsetResolver(
                new TypeFinder([delegateDefinition]),
                new FieldRepository(delegateFields),
                delegateState)));
        Assert.Equal(0, offsets.Target);
        Assert.Equal(4, offsets.MethodId);
        Assert.Equal(8, offsets.InvocationList);
        Assert.Equal(12, offsets.InvocationCount);
    }

    [Fact]
    public void DelegateFieldOffsetResolverAcceptsPrivateTargetAbiField()
    {
        var assembly = new AssemblyIdentity("DelegateAbiTests");
        var typeKey = new EntityKey(assembly, 1);
        var delegateFields = ImmutableArray.Create(
            "_target",
            "MethodId",
            "_invocationList",
            "_invocationCount")
            .Select((name, index) => new FieldDefinitionModel(
                new EntityKey(assembly, 2 + index),
                typeKey,
                name,
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                false))
            .ToArray();
        var delegateDefinition = new TypeDefinitionModel(
            typeKey,
            "System",
            "Delegate",
            false,
            [.. delegateFields.Select(member => member.Key)],
            []);
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        foreach (var (member, offset) in delegateFields.Select((member, index) =>
                     (member, index * 4)))
        {
            state.ValueLayouts.Fields.Add(member.Key, new FieldLayout(offset));
        }

        var offsets = ResolveDelegateOffsets(As<IDelegateFieldOffsetResolver>(
            new DelegateFieldOffsetResolver(
                new TypeFinder([delegateDefinition]),
                new FieldRepository(delegateFields),
                state)));

        Assert.Equal(0, offsets.Target);
        Assert.Equal(4, offsets.MethodId);
        Assert.Equal(8, offsets.InvocationList);
        Assert.Equal(12, offsets.InvocationCount);
    }

    [Fact]
    public void DelegateFieldOffsetResolverRejectsAPartialDelegateAbi()
    {
        var assembly = new AssemblyIdentity("DelegateAbiTests");
        var typeKey = new EntityKey(assembly, 1);
        var target = new FieldDefinitionModel(
            new EntityKey(assembly, 2),
            typeKey,
            "Target",
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            false);
        var definition = new TypeDefinitionModel(
            typeKey,
            "System",
            "Delegate",
            false,
            [target.Key],
            []);
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        state.ValueLayouts.Fields.Add(target.Key, new FieldLayout(0));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ResolveDelegateOffsets(As<IDelegateFieldOffsetResolver>(
                new DelegateFieldOffsetResolver(
                    new TypeFinder([definition]),
                    new FieldRepository([target]),
                    state))));

        Assert.Equal("The delegate ABI field 'MethodId' was not found.", exception.Message);
    }

    [Fact]
    public void LayoutActorsRejectNullCapabilities()
    {
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var types = EmptyLayouts(target);
        var staticState = new ManagedStaticDataBuildState();
        var typeState = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var finder = new TypeFinder([]);
        var definitions = new TypeDefinitions([]);
        var typeRepository = new TypeRepository([]);
        var fields = new FieldRepository([]);
        var identities = new UnusedTypeIdentities();
        var entityBases = new BaseTypes();
        var identityBases = new BaseTypes();
        var program = EmptyProgram();
        var bitmaps = new StaticReferenceBitmapBuilder();
        var valueResolver = new FixedValueLayoutResolver();
        var exceptionNames = new ExceptionTypeNameResolver();
        var objectLayouts = new RecordingObjectLayoutBuilder();
        var buildPhase = new RecordingStaticBuildPhase();
        var objectPhase = new RecordingBuildPhase();
        var resolvePhase = new RecordingResolvePhase();
        var offsetPhase = new RecordingDelegateOffsetPhase();
        var layoutResolver = new ObjectLayoutResolver(definitions, types);

        ThrowsNull(() => new ObjectLayoutResolver(null!, types));
        ThrowsNull(() => new ObjectLayoutResolver(definitions, null!));

        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            null!, definitions, identityBases, layoutResolver, types, target, staticState, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, null!, identityBases, layoutResolver, types, target, staticState, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, null!, layoutResolver, types, target, staticState, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, identityBases, null!, types, target, staticState, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, identityBases, layoutResolver, null!, target, staticState, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, identityBases, layoutResolver, types, null!, staticState, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, identityBases, layoutResolver, types, target, null!, bitmaps));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, identityBases, layoutResolver, types, target, staticState, null!));
        ThrowsNull(() => new ConstructedTypeDescriptorBuilder(
            finder, definitions, identityBases, layoutResolver, types, target, staticState,
            bitmaps, null!));

        ThrowsNull(() => new DelegateFieldOffsetResolver(null!, fields, typeState));
        ThrowsNull(() => new DelegateFieldOffsetResolver(finder, null!, typeState));
        ThrowsNull(() => new DelegateFieldOffsetResolver(finder, fields, null!));

        ThrowsNull(() => new ExceptionObjectBuilder(
            null!, fields, program, types, target, staticState, exceptionNames));
        ThrowsNull(() => new ExceptionObjectBuilder(
            finder, null!, program, types, target, staticState, exceptionNames));
        ThrowsNull(() => new ExceptionObjectBuilder(
            finder, fields, null!, types, target, staticState, exceptionNames));
        ThrowsNull(() => new ExceptionObjectBuilder(
            finder, fields, program, null!, target, staticState, exceptionNames));
        ThrowsNull(() => new ExceptionObjectBuilder(
            finder, fields, program, types, null!, staticState, exceptionNames));
        ThrowsNull(() => new ExceptionObjectBuilder(
            finder, fields, program, types, target, null!, exceptionNames));
        ThrowsNull(() => new ExceptionObjectBuilder(
            finder, fields, program, types, target, staticState, null!));

        ThrowsNull(() => new ImplicitObjectLayoutBuilder(
            null!, typeRepository, program, objectLayouts, exceptionNames, typeState));
        ThrowsNull(() => new ImplicitObjectLayoutBuilder(
            finder, null!, program, objectLayouts, exceptionNames, typeState));
        ThrowsNull(() => new ImplicitObjectLayoutBuilder(
            finder, typeRepository, null!, objectLayouts, exceptionNames, typeState));
        ThrowsNull(() => new ImplicitObjectLayoutBuilder(
            finder, typeRepository, program, null!, exceptionNames, typeState));
        ThrowsNull(() => new ImplicitObjectLayoutBuilder(
            finder, typeRepository, program, objectLayouts, null!, typeState));
        ThrowsNull(() => new ImplicitObjectLayoutBuilder(
            finder, typeRepository, program, objectLayouts, exceptionNames, null!));

        ThrowsNull(() => new InstanceFieldLayoutProvider(null!));
        ThrowsNull(() => new ManagedExceptionObjectProvider(null!));
        ThrowsNull(() => new StaticDataLayoutProvider(null!));
        ThrowsNull(() => new StaticFieldLayoutProvider(null!));
        ThrowsNull(() => new TypeLayoutProvider(null!, EmptySnapshot(target)));
        ThrowsNull(() => new TypeLayoutProvider(definitions, null!));
        ThrowsNull(() => new ValueLayoutProvider(null!));

        ThrowsNull(() => new ManagedLayoutCompiler(null!,
            new ManagedStaticDataBuilderFactory(exceptionNames)));
        ThrowsNull(() => new ManagedLayoutCompiler(
            new ManagedTypeLayoutCompilerFactory(new ValueLayoutResolverFactory(new ExplicitValueLayoutResolver()), new ManagedObjectLayoutBuilderFactory(new ExplicitValueLayoutResolver()), exceptionNames), null!));

        var valueStorageIdentities = identities;

        ThrowsNull(() => new ManagedObjectLayoutBuilder(null!, fields, finder, definitions, identities, entityBases, identityBases, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, null!, finder, definitions, identities, entityBases, identityBases, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, null!, definitions, identities, entityBases, identityBases, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, null!, identities, entityBases, identityBases, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, null!, entityBases, identityBases, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, identities, null!, identityBases, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, identities, entityBases, null!, target, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, identities, entityBases, identityBases, null!, typeState, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, identities, entityBases, identityBases, target, null!, valueResolver, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, identities, entityBases, identityBases, target, typeState, null!, new ExplicitValueLayoutResolver()));
        ThrowsNull(() => new ManagedObjectLayoutBuilder(typeRepository, fields, finder, definitions, identities, entityBases, identityBases, target, typeState, valueResolver, null!));

        ThrowsNull(() => new ManagedStaticDataBuilder(
            null!, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, null!, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, null!, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, null!, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, null!, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, null!, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, null!,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            null!, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, null!, buildPhase, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, null!, buildPhase, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, null!, buildPhase, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, null!, buildPhase));
        ThrowsNull(() => new ManagedStaticDataBuilder(
            types, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, null!));
        var nullTargetTypes = types with { Target = null! };
        ThrowsNull(() => new ManagedStaticDataBuilder(
            nullTargetTypes, staticState, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase,
            buildPhase, buildPhase, buildPhase, buildPhase, buildPhase, buildPhase));

        ThrowsNull(() => new ManagedStaticDataBuilderFactory(null!));
        ThrowsNull(() => new ManagedStaticDataBuilderFactory(exceptionNames, null!));
        ThrowsNull(() => new ManagedTypeLayoutBuildState(null!));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            null!, target, objectPhase, resolvePhase, resolvePhase, objectPhase,
            resolvePhase, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, null!, objectPhase, resolvePhase, resolvePhase, objectPhase,
            resolvePhase, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, target, null!, resolvePhase, resolvePhase, objectPhase,
            resolvePhase, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, target, objectPhase, null!, resolvePhase, objectPhase,
            resolvePhase, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, target, objectPhase, resolvePhase, null!, objectPhase,
            resolvePhase, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, target, objectPhase, resolvePhase, resolvePhase, null!,
            resolvePhase, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, target, objectPhase, resolvePhase, resolvePhase, objectPhase,
            null!, offsetPhase));
        ThrowsNull(() => new ManagedTypeLayoutCompiler(
            typeState, target, objectPhase, resolvePhase, resolvePhase, objectPhase,
            resolvePhase, null!));
        ThrowsNull(() => new ManagedTypeLayoutCompilerFactory(null!, new ManagedObjectLayoutBuilderFactory(new ExplicitValueLayoutResolver()), exceptionNames));
        ThrowsNull(() => new ManagedTypeLayoutCompilerFactory(new ValueLayoutResolverFactory(new ExplicitValueLayoutResolver()), null!, exceptionNames));
        ThrowsNull(() => new ManagedTypeLayoutCompilerFactory(new ValueLayoutResolverFactory(new ExplicitValueLayoutResolver()), new ManagedObjectLayoutBuilderFactory(new ExplicitValueLayoutResolver()), null!));

        ThrowsNull(() => new MethodValueLayoutResolver(null!, valueResolver));
        ThrowsNull(() => new MethodValueLayoutResolver(program, null!));
        ThrowsNull(() => new ReachableObjectLayoutBuilder(
            null!, program, objectLayouts));
        ThrowsNull(() => new ReachableObjectLayoutBuilder(
            typeRepository, null!, objectLayouts));
        ThrowsNull(() => new ReachableObjectLayoutBuilder(
            typeRepository, program, null!));
        ThrowsNull(() => new StaticFieldStorageBuilder(
            null!, fields, program, types, staticState));
        ThrowsNull(() => new StaticFieldStorageBuilder(
            typeRepository, null!, program, types, staticState));
        ThrowsNull(() => new StaticFieldStorageBuilder(
            typeRepository, fields, null!, types, staticState));
        ThrowsNull(() => new StaticFieldStorageBuilder(
            typeRepository, fields, program, null!, staticState));
        ThrowsNull(() => new StaticFieldStorageBuilder(
            typeRepository, fields, program, types, null!));
        ThrowsNull(() => new StaticFieldValueLayoutResolver(null!, program, valueResolver));
        ThrowsNull(() => new StaticFieldValueLayoutResolver(fields, null!, valueResolver));
        ThrowsNull(() => new StaticFieldValueLayoutResolver(fields, program, null!));
        ThrowsNull(() => new StringDataBuilder(null!, program, types, target, staticState));
        ThrowsNull(() => new StringDataBuilder(finder, null!, types, target, staticState));
        ThrowsNull(() => new StringDataBuilder(finder, program, null!, target, staticState));
        ThrowsNull(() => new StringDataBuilder(finder, program, types, null!, staticState));
        ThrowsNull(() => new StringDataBuilder(finder, program, types, target, null!));
        ThrowsNull(() => new TypeDescriptorBuilder(
            null!, definitions, identityBases, layoutResolver, program, types, target, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, null!, identityBases, layoutResolver, program, types, target, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, null!, layoutResolver, program, types, target, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, null!, program, types, target, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, layoutResolver, null!, types, target, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, layoutResolver, program, null!, target, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, layoutResolver, program, types, null!, staticState, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, layoutResolver, program, types, target, null!, bitmaps));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, layoutResolver, program, types, target, staticState, null!));
        ThrowsNull(() => new TypeDescriptorBuilder(
            identities, definitions, identityBases, layoutResolver, program, types, target,
            staticState, bitmaps, null!));
        ThrowsNull(() => new ValueLayoutResolver(
            null!, fields, new ExplicitValueLayoutResolver(), target,
            new ManagedValueLayoutState()));
        ThrowsNull(() => new ValueLayoutResolver(
            definitions, null!, new ExplicitValueLayoutResolver(), target,
            new ManagedValueLayoutState()));
        ThrowsNull(() => new ValueLayoutResolver(
            definitions, fields, null!, target, new ManagedValueLayoutState()));
        ThrowsNull(() => new ValueLayoutResolver(
            definitions, fields, new ExplicitValueLayoutResolver(), null!,
            new ManagedValueLayoutState()));
        ThrowsNull(() => new ValueLayoutResolver(
            definitions, fields, new ExplicitValueLayoutResolver(), target, null!));
        ThrowsNull(() => new ValueTypeDescriptorBuilder(
            null!, valueStorageIdentities, types, target, staticState, bitmaps));
        ThrowsNull(() => new ValueTypeDescriptorBuilder(
            typeRepository, null!, types, target, staticState, bitmaps));
        ThrowsNull(() => new ValueTypeDescriptorBuilder(
            typeRepository, valueStorageIdentities, null!, target, staticState, bitmaps));
        ThrowsNull(() => new ValueTypeDescriptorBuilder(
            typeRepository, valueStorageIdentities, types, null!, staticState, bitmaps));
        ThrowsNull(() => new ValueTypeDescriptorBuilder(
            typeRepository, valueStorageIdentities, types, target, null!, bitmaps));
        ThrowsNull(() => new ValueTypeDescriptorBuilder(
            typeRepository, valueStorageIdentities, types, target, staticState, null!));
        ThrowsNull(() => new ValueTypeLayoutResolver(null!, fields, identities, valueResolver, typeState, []));
        ThrowsNull(() => new ValueTypeLayoutResolver(typeRepository, null!, identities, valueResolver, typeState, []));
        ThrowsNull(() => new ValueTypeLayoutResolver(typeRepository, fields, null!, valueResolver, typeState, []));
        ThrowsNull(() => new ValueTypeLayoutResolver(typeRepository, fields, identities, null!, typeState, []));
        ThrowsNull(() => new ValueTypeLayoutResolver(typeRepository, fields, identities, valueResolver, null!, []));
    }

    [Fact]
    public void LayoutFactoriesComposeThroughCanonicalTypeIdentityCapability()
    {

        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var types = EmptyLayouts(target);
        var staticState = new ManagedStaticDataBuildState();
        var typeState = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var finder = new TypeFinder([]);
        var definitions = new TypeDefinitions([]);
        var typeRepository = new TypeRepository([]);
        var fields = new FieldRepository([]);
        var identities = new UnusedTypeIdentities();
        var entityBases = new BaseTypes();
        var identityBases = new BaseTypes();
        var program = EmptyProgram();
        var bitmaps = new StaticReferenceBitmapBuilder();
        var valueResolver = new FixedValueLayoutResolver();
        var exceptionNames = new ExceptionTypeNameResolver();
        var objectLayouts = new RecordingObjectLayoutBuilder();
        var buildPhase = new RecordingStaticBuildPhase();
        var objectPhase = new RecordingBuildPhase();
        var resolvePhase = new RecordingResolvePhase();
        var offsetPhase = new RecordingDelegateOffsetPhase();
        var layoutResolver = new ObjectLayoutResolver(definitions, types);

        var metadata = new MetadataCompilationSnapshot(
            ImmutableArray<ManagedAssembly>.Empty,
            new AssemblyIdentity("LayoutFactoryTests"),
            ImmutableArray<TypeDefinitionModel>.Empty,
            ImmutableArray<MethodDefinitionModel>.Empty,
            ImmutableArray<MethodDefinitionModel>.Empty,
            ImmutableDictionary<string, string>.Empty);
        var objectFactory = new ManagedObjectLayoutBuilderFactory(new ExplicitValueLayoutResolver());
        var staticFactory = new ManagedStaticDataBuilderFactory(exceptionNames);
        var valueFactory = new ValueLayoutResolverFactory(new ExplicitValueLayoutResolver());
        var compilerFactory = new ManagedTypeLayoutCompilerFactory(valueFactory, objectFactory, exceptionNames);

        Assert.NotNull(objectFactory.Create(
            typeRepository,
            fields,
            finder,
            definitions,
            identities,
            entityBases,
            identityBases,
            target,
            typeState,
            valueResolver));
        Assert.NotNull(staticFactory.Create(
            metadata,
            typeRepository,
            fields,
            finder,
            definitions,
            identities,
            identityBases,
            program,
            types));
        Assert.NotNull(compilerFactory.Create(
            metadata,
            typeRepository,
            fields,
            finder,
            definitions,
            identities,
            entityBases,
            identityBases,
            program,
            target));
    }

    [Fact]
    public void TypeLayoutCompilerSequencesInjectedPhaseContracts()
    {
        var builds = new RecordingBuildPhase();
        var resolutions = new RecordingResolvePhase();
        var offsets = new RecordingDelegateOffsetPhase();
        var compiler = new ManagedTypeLayoutCompiler(
            new ManagedTypeLayoutBuildState(new ManagedValueLayoutState()),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            builds,
            resolutions,
            resolutions,
            builds,
            resolutions,
            offsets);

        var layouts = compiler.Compile();

        Assert.Equal(2, builds.Calls);
        Assert.Equal(3, resolutions.Calls);
        Assert.Equal(-1, layouts.DelegateTargetOffset);
    }

    [Fact]
    public void StaticDataBuilderSequencesInjectedPhaseContracts()
    {
        var phase = new RecordingStaticBuildPhase();
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            target,
            -1,
            -1,
            -1,
            -1);
        var builder = new ManagedStaticDataBuilder(
            types,
            new ManagedStaticDataBuildState(),
            phase,
            phase,
            phase,
            phase,
            phase,
            phase,
            phase,
            phase,
            phase,
            phase,
            phase);

        builder.Build();

        Assert.Equal(11, phase.Calls);
    }

    [Fact]
    public void ObjectLayoutBuilderBuildsAndCachesAReferenceTypeThroughItsContract()
    {
        var assembly = new AssemblyIdentity("ObjectLayoutPhaseTests");
        var key = new EntityKey(assembly, 1);
        var definition = new TypeDefinitionModel(
            key,
            "Tests",
            "Node",
            false,
            [],
            []);
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var builder = As<IManagedObjectLayoutBuilder>(new ManagedObjectLayoutBuilder(new TypeRepository([definition]), new FieldRepository([]), new TypeFinder([definition]), new TypeDefinitions([definition]), new TypeIdentities(assembly, "Tests.Node", key), new BaseTypes(), new BaseTypes(), target, state, new FixedValueLayoutResolver(), new ExplicitValueLayoutResolver()));

        var first = BuildObject(builder, key);
        var second = BuildObject(builder, key);

        Assert.Equal(first, second);
        Assert.Equal(1, first.TypeId);
        Assert.Equal(target.ObjectHeaderSize, first.Size);
        Assert.Empty(first.ReferenceOffsets);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void VectorAndRankOneArrayLayoutsStayDistinctInEitherDiscoveryOrder(
        WasmTarget wasmTarget)
    {
        foreach (var rankOneFirst in new[] { false, true })
        {
            var assembly = new AssemblyIdentity("ArrayShapeIdentityTests");
            var arrayKey = new EntityKey(assembly, 1);
            var arrayDefinition = Definition(arrayKey, "System.Array", false);
            var element = CliTypeIdentity.Named(
                assembly, "Tests", "Element", isValueType: false);
            var vector = CliTypeIdentity.SzArray(element);
            var rankOne = CliTypeIdentity.Array(element, 1);
            var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
            var builder = As<IManagedObjectLayoutBuilder>(new ManagedObjectLayoutBuilder(
                new TypeRepository([arrayDefinition]),
                new FieldRepository([]),
                new TypeFinder([arrayDefinition]),
                new TypeDefinitions([arrayDefinition]),
                new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
                {
                    [arrayKey] = CliTypeIdentity.Named(
                        assembly, "System", "Array", isValueType: false),
                }),
                new BaseTypes(),
                new BaseTypes(),
                WasmTargetLayout.For(wasmTarget),
                state,
                new FixedValueLayoutResolver(), new ExplicitValueLayoutResolver()));

            if (rankOneFirst)
            {
                builder.Build(rankOne);
                builder.Build(vector);
            }
            else
            {
                builder.Build(vector);
                builder.Build(rankOne);
            }

            var vectorLayout = state.ConstructedObjects[vector];
            var rankOneLayout = state.ConstructedObjects[rankOne];
            Assert.Equal(2, state.ConstructedObjects.Count);
            Assert.NotEqual(vectorLayout.TypeId, rankOneLayout.TypeId);
            Assert.True(rankOneLayout.Size > vectorLayout.Size);
            Assert.Equal(
                vectorLayout.ReferenceOffsets.Length + 1,
                rankOneLayout.ReferenceOffsets.Length);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ObjectLayoutBuilderCreatesMetadataLayoutWithoutValueStorage(
        WasmTarget wasmTarget)
    {
        var assembly = new AssemblyIdentity("ObjectLayoutMetadataOnlyTests");
        var key = new EntityKey(assembly, 1);
        var definition = Definition(key, "System.Void", true);
        var identity = CliTypeIdentity.Primitive("void", CliValueKind.Void);
        var identities = new MappingIdentities(
            new Dictionary<EntityKey, CliTypeIdentity> { [key] = identity });
        var target = WasmTargetLayout.For(wasmTarget);
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var values = new RecordingValueLayoutResolver();
        var builder = As<IManagedObjectLayoutBuilder>(new ManagedObjectLayoutBuilder(
            new TypeRepository([definition]),
            new FieldRepository([]),
            new TypeFinder([definition]),
            new TypeDefinitions([definition]),
            identities,
            new BaseTypes(),
            new BaseTypes(),
            target,
            state,
            values, new ExplicitValueLayoutResolver()));

        var layout = BuildObject(builder, key);

        Assert.Equal(1, layout.TypeId);
        Assert.Equal(target.ObjectHeaderSize, layout.Size);
        Assert.Empty(layout.ReferenceOffsets);
        Assert.Empty(values.Types);
        Assert.Equal(layout, state.Objects[key]);
        Assert.Equal(layout, state.ObjectIdentities[identity]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ObjectLayoutBuilderCreatesMetadataOnlyOpenGenericDefinitionLayout(
        WasmTarget wasmTarget)
    {
        var assembly = new AssemblyIdentity("OpenGenericLayoutTests");
        var key = new EntityKey(assembly, 1);
        var definition = Definition(key, "Tests.Template`1", false) with
        {
            GenericArity = 1,
        };
        var identity = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Template`1",
            isValueType: false);
        var identities = new MappingIdentities(
            new Dictionary<EntityKey, CliTypeIdentity> { [key] = identity });
        var target = WasmTargetLayout.For(wasmTarget);
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var values = new RecordingValueLayoutResolver();
        var builder = As<IManagedObjectLayoutBuilder>(new ManagedObjectLayoutBuilder(
            new TypeRepository([definition]),
            new FieldRepository([]),
            new TypeFinder([definition]),
            new TypeDefinitions([definition]),
            identities,
            new BaseTypes(),
            new BaseTypes(),
            target,
            state,
            values, new ExplicitValueLayoutResolver()));

        var layout = BuildObject(builder, key);

        Assert.Equal(1, layout.TypeId);
        Assert.Equal(target.ObjectHeaderSize, layout.Size);
        Assert.Empty(layout.ReferenceOffsets);
        Assert.Empty(values.Types);
        Assert.Equal(layout, state.Objects[key]);
        Assert.Equal(layout, state.ObjectIdentities[identity]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ObjectLayoutBuilderCreatesDistinctDescriptorOnlyElementModifierLayouts(
        WasmTarget wasmTarget)
    {
        var assembly = new AssemblyIdentity("ElementModifierLayoutTests");
        var element = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Value",
            isValueType: true);
        var byReference = CliTypeIdentity.ManagedByReference(element);
        var pointer = CliTypeIdentity.UnmanagedPointer(element);
        var target = WasmTargetLayout.For(wasmTarget);
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var values = new RecordingValueLayoutResolver();
        var builder = As<IManagedObjectLayoutBuilder>(new ManagedObjectLayoutBuilder(
            new TypeRepository([]),
            new FieldRepository([]),
            new TypeFinder([]),
            new TypeDefinitions([]),
            new UnusedTypeIdentities(),
            new BaseTypes(),
            new BaseTypes(),
            target,
            state,
            values, new ExplicitValueLayoutResolver()));

        var byReferenceLayout = builder.Build(byReference);
        var pointerLayout = builder.Build(pointer);

        Assert.Equal(target.ObjectHeaderSize, byReferenceLayout.Size);
        Assert.Equal(target.ObjectHeaderSize, pointerLayout.Size);
        Assert.Empty(byReferenceLayout.ReferenceOffsets);
        Assert.Empty(pointerLayout.ReferenceOffsets);
        Assert.NotEqual(byReferenceLayout.TypeId, pointerLayout.TypeId);
        Assert.Equal(byReferenceLayout, builder.Build(byReference));
        Assert.Equal(pointerLayout, builder.Build(pointer));
        Assert.Equal(2, state.ConstructedObjects.Count);
        Assert.Empty(values.Types);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignableTypeMetadataSkipsElementModifierExpansion(bool unmanaged)
    {
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var modifier = unmanaged
            ? CliTypeIdentity.UnmanagedPointer(element)
            : CliTypeIdentity.ManagedByReference(element);
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var builder = As<IAssignableTypeMetadataBuilder>(
            new AssignableTypeMetadataBuilder(
                new UnusedTypeIdentities(),
                new TypeDefinitions([]),
                new RejectingTypeRelationshipClassifier(),
                UnboundedTypeRelationshipKeys.Instance,
                EmptyLayouts(target),
                new ManagedStaticDataBuildState()));

        var layout = builder.Build(modifier);

        Assert.Equal(default, layout);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConstructedDescriptorsRegisterElementModifiersWithoutObjectSemantics(
        WasmTarget wasmTarget)
    {
        var assembly = new AssemblyIdentity("ElementModifierDescriptorTests");
        var objectKey = new EntityKey(assembly, 1);
        var objectDefinition = Definition(objectKey, "System.Object", false);
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var byReference = CliTypeIdentity.ManagedByReference(element);
        var pointer = CliTypeIdentity.UnmanagedPointer(element);
        var target = WasmTargetLayout.For(wasmTarget);
        var objectLayout = new ObjectLayout(1, target.ObjectHeaderSize, []);
        var byReferenceLayout = new ObjectLayout(2, target.ObjectHeaderSize, []);
        var pointerLayout = new ObjectLayout(3, target.ObjectHeaderSize, []);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(
                objectKey,
                objectLayout),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(byReference, byReferenceLayout)
                .Add(pointer, pointerLayout),
            new ManagedValueLayoutState(),
            target,
            -1,
            -1,
            -1,
            -1);
        var definitions = new TypeDefinitions([objectDefinition]);
        var state = new ManagedStaticDataBuildState();

        BuildConstructedTypeDescriptors(new ConstructedTypeDescriptorBuilder(
            new TypeFinder([objectDefinition]),
            definitions,
            new BaseTypes(),
            new ObjectLayoutResolver(definitions, types),
            types,
            target,
            state,
            new StaticReferenceBitmapBuilder()));

        Assert.Equal(2, state.ConstructedTypeDescriptors.Count);
        Assert.All(state.ConstructedTypeDescriptors, descriptor =>
        {
            Assert.Equal(0, descriptor.BaseTypeId);
            Assert.Equal(0, descriptor.AssignableTypeIdsAddress);
            Assert.Equal(0, descriptor.AssignableTypeIdCount);
            Assert.False(descriptor.IsInterface);
            Assert.Null(descriptor.Finalizer);
        });
        Assert.Contains(
            state.ConstructedTypeDescriptors,
            descriptor => descriptor.Type == byReference &&
                descriptor.TypeId == byReferenceLayout.TypeId);
        Assert.Contains(
            state.ConstructedTypeDescriptors,
            descriptor => descriptor.Type == pointer &&
                descriptor.TypeId == pointerLayout.TypeId);
    }

    [Fact]
    public void ValueTypeDescriptorsPreserveOpenGenericMetadataWithoutValueStorage()
    {
        var key = new EntityKey(new("GenericValueDescriptorTests"), 1);
        var definition = Definition(key, "Tests.Cell`1", true) with { GenericArity = 1 };
        var identity = CliTypeIdentity.FromDefinition(definition);
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var descriptor = new ObjectLayout(1, target.ObjectHeaderSize, []);
        var types = EmptyLayouts(target) with
        {
            Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(key, descriptor),
        };
        var state = new ManagedStaticDataBuildState();
        var initialCursor = state.Cursor;

        BuildValueTypeDescriptors(new ValueTypeDescriptorBuilder(
            new TypeRepository([definition]),
            new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity> { [key] = identity }),
            types, target, state, new UnusedReferenceBitmapBuilder()));

        Assert.Empty(state.ValueTypeDescriptors);
        Assert.Empty(state.Segments);
        Assert.Equal(initialCursor, state.Cursor);
        Assert.Equal(descriptor, types.Objects[key]);
    }

    private sealed class UnusedReferenceBitmapBuilder : IStaticReferenceBitmapBuilder
    {
        public byte[] Build(ImmutableArray<int> referenceOffsets, int bitCount, int referenceSize) =>
            throw new InvalidOperationException("Metadata-only definitions have no value bitmap.");
    }

    [Fact]
    public void ValueTypeDescriptorsSkipTypesWithoutRuntimeStorage()
    {
        var assembly = new AssemblyIdentity("ValueDescriptorMetadataOnlyTests");
        var key = new EntityKey(assembly, 1);
        var definition = Definition(key, "System.Void", true);
        var identity = CliTypeIdentity.Primitive("void", CliValueKind.Void);
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var boxed = new ObjectLayout(1, target.ObjectHeaderSize, []);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(key, boxed),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(identity, boxed),
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            target,
            -1,
            -1,
            -1,
            -1);
        var state = new ManagedStaticDataBuildState();

        new ValueTypeDescriptorBuilder(
            new TypeRepository([definition]),
            new MappingIdentities(
                new Dictionary<EntityKey, CliTypeIdentity> { [key] = identity }),
            types,
            target,
            state,
            new StaticReferenceBitmapBuilder()).Build();

        Assert.Empty(state.ValueTypeDescriptors);
        Assert.Empty(state.Segments);
    }

    [Fact]
    public void ValueTypeDescriptorsSkipDescriptorOnlyElementModifiers()
    {
        var element = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var byReference = CliTypeIdentity.ManagedByReference(element);
        var pointer = CliTypeIdentity.UnmanagedPointer(element);
        var target = WasmTargetLayout.For(WasmTarget.Wasm32);
        var layout = new ObjectLayout(1, target.ObjectHeaderSize, []);
        var types = EmptyLayouts(target) with
        {
            ConstructedObjects =
                ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                    .Add(byReference, layout)
                    .Add(pointer, layout with { TypeId = 2 }),
        };
        var state = new ManagedStaticDataBuildState();

        BuildValueTypeDescriptors(new ValueTypeDescriptorBuilder(
            new TypeRepository([]),
            new UnusedTypeIdentities(),
            types,
            target,
            state,
            new StaticReferenceBitmapBuilder()));

        Assert.Empty(state.ValueTypeDescriptors);
        Assert.Empty(state.Segments);
    }

    [Fact]
    public void ObjectLayoutBuilderKeysOrdinaryTypesByAssemblyIdentity()
    {
        var firstAssembly = new AssemblyIdentity("First");
        var secondAssembly = new AssemblyIdentity("Second");
        var firstPrivateKey = new EntityKey(firstAssembly, 1);
        var secondPrivateKey = new EntityKey(secondAssembly, 1);
        var canonicalTypeKey = new EntityKey(firstAssembly, 2);
        var firstPrivate = Definition(
            firstPrivateKey,
            "<PrivateImplementationDetails>",
            false);
        var secondPrivate = Definition(
            secondPrivateKey,
            "<PrivateImplementationDetails>",
            false);
        var canonicalType = Definition(canonicalTypeKey, "System.Type", false);
        var definitions = new[]
        {
            firstPrivate,
            secondPrivate,
            canonicalType,
        };
        var identities = definitions.ToDictionary(
            definition => definition.Key,
            definition => CliTypeIdentity.Named(
                definition.Key.Assembly,
                definition.Namespace,
                definition.Name,
                false));
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var builder = As<IManagedObjectLayoutBuilder>(new ManagedObjectLayoutBuilder(new TypeRepository(definitions), new FieldRepository([]), new MappedTypeFinder(firstPrivate, canonicalType), new TypeDefinitions([canonicalType]), new MappingIdentities(identities), new BaseTypes(), new BaseTypes(), WasmTargetLayout.For(WasmTarget.Wasm32), state, new FixedValueLayoutResolver(), new ExplicitValueLayoutResolver()));

        _ = BuildObject(builder, firstPrivateKey);
        _ = BuildObject(builder, secondPrivateKey);
        var canonicalLayout = BuildObject(builder, canonicalTypeKey);

        Assert.Equal(3, state.Objects.Count);
        Assert.Single(state.ObjectsByName);
        Assert.Equal(canonicalLayout, state.ObjectsByName["System.Type"]);
        Assert.DoesNotContain("<PrivateImplementationDetails>", state.ObjectsByName);
    }

    [Fact]
    public void ObjectLayoutBuilderCoversEntityAndConstructedShapeBranches()
    {
        var assembly = new AssemblyIdentity("ObjectLayoutBranchTests");
        var baseReferenceKey = new EntityKey(assembly, 1);
        var referenceKey = new EntityKey(assembly, 2);
        var arrayKey = new EntityKey(assembly, 3);
        var stringKey = new EntityKey(assembly, 4);
        var baseValueKey = new EntityKey(assembly, 5);
        var valueKey = new EntityKey(assembly, 6);
        var baseReferenceIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "BaseReference", false);
        var referenceIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "Reference", false);
        var baseValueIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "BaseValue", true);
        var valueIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "Value", true);
        var genericBaseDefinition = CliTypeIdentity.Named(
            assembly, "Tests", "GenericBase`1", false);
        var genericDefinition = CliTypeIdentity.Named(
            assembly, "Tests", "Generic`1", false);
        var genericBase = CliTypeIdentity.GenericInstantiation(
            genericBaseDefinition, [referenceIdentity]);
        var generic = CliTypeIdentity.GenericInstantiation(
            genericDefinition, [referenceIdentity]);
        var baseReferenceFieldKey = new EntityKey(assembly, 10);
        var referenceFieldKey = new EntityKey(assembly, 11);
        var staticFieldKey = new EntityKey(assembly, 12);
        var genericFieldKey = new EntityKey(assembly, 13);
        var definitions = new[]
        {
            new TypeDefinitionModel(
                baseReferenceKey, "Tests", "BaseReference", false,
                [baseReferenceFieldKey], []),
            new TypeDefinitionModel(
                referenceKey, "Tests", "Reference", false,
                [referenceFieldKey, staticFieldKey], []),
            Definition(arrayKey, "System.Array", false),
            Definition(stringKey, "System.String", false),
            Definition(baseValueKey, "Tests.BaseValue", true),
            Definition(valueKey, "Tests.Value", true),
            new TypeDefinitionModel(
                new EntityKey(assembly, 7), "Tests", "GenericBase`1", false, [], []),
            new TypeDefinitionModel(
                new EntityKey(assembly, 8), "Tests", "Generic`1", false,
                [genericFieldKey], []),
        };
        var fields = new[]
        {
            new FieldDefinitionModel(
                baseReferenceFieldKey,
                baseReferenceKey,
                "BaseValue",
                referenceIdentity,
                false),
            new FieldDefinitionModel(
                referenceFieldKey,
                referenceKey,
                "Value",
                referenceIdentity,
                false),
            new FieldDefinitionModel(
                staticFieldKey,
                referenceKey,
                "StaticValue",
                referenceIdentity,
                true),
            new FieldDefinitionModel(
                genericFieldKey,
                new EntityKey(assembly, 8),
                "Value",
                referenceIdentity,
                false),
        };
        var state = new ManagedTypeLayoutBuildState(new ManagedValueLayoutState());
        var identities = new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
        {
            [baseReferenceKey] = baseReferenceIdentity,
            [referenceKey] = referenceIdentity,
            [arrayKey] = CliTypeIdentity.Named(assembly, "System", "Array", false),
            [stringKey] = CliTypeIdentity.Named(assembly, "System", "String", false),
            [baseValueKey] = baseValueIdentity,
            [valueKey] = valueIdentity,
        });
        var entityBases = new MappingEntityBases(new Dictionary<EntityKey, EntityKey?>
        {
            [referenceKey] = baseReferenceKey,
            [valueKey] = baseValueKey,
        });
        var identityBases = new MappingIdentityBases(new Dictionary<CliTypeIdentity,
            CliTypeIdentity?>
        {
            [valueIdentity] = baseValueIdentity,
            [generic] = genericBase,
        });
        var builder = new ManagedObjectLayoutBuilder(new TypeRepository(definitions), new FieldRepository(fields), new TypeFinder(definitions), new TypeDefinitions(definitions), identities, entityBases, identityBases, WasmTargetLayout.For(WasmTarget.Wasm32), state, new ReferenceValueLayoutResolver(), new ExplicitValueLayoutResolver());

        builder.Build(valueKey);
        builder.Build(referenceKey);
        builder.Build(arrayKey);
        builder.Build(stringKey);
        builder.Build(valueIdentity);
        builder.Build(CliTypeIdentity.SzArray(valueIdentity));
        var rectangular = builder.Build(CliTypeIdentity.Array(valueIdentity, 2));
        builder.Build(generic);
        Assert.Equal(builder.Build(generic), builder.Build(generic));
        Assert.NotEmpty(rectangular.ReferenceOffsets);
        Assert.NotEmpty(state.Objects);
        Assert.NotEmpty(state.ConstructedObjects);
        Assert.Contains(referenceFieldKey, state.ValueLayouts.Fields.Keys);
        Assert.Contains(
            new FieldInstanceModel(
                fields[3], generic, referenceIdentity).CanonicalName,
            state.ValueLayouts.ConstructedFields.Keys);
    }

    [Fact]
    public void StaticDataSectionsBuildIndependentlyThroughOneActionContracts()
    {
        var assembly = new AssemblyIdentity("StaticDataPhaseTests");
        var stringKey = new EntityKey(assembly, 1);
        var exceptionKey = new EntityKey(assembly, 2);
        var arrayKey = new EntityKey(assembly, 3);
        var valueKey = new EntityKey(assembly, 4);
        var stringDefinition = Definition(stringKey, "System.String", false);
        var exceptionDefinition = Definition(
            exceptionKey,
            "System.OutOfMemoryException",
            false);
        var arrayDefinition = Definition(arrayKey, "System.Array", false);
        var valueDefinition = Definition(valueKey, "Tests.Value", true);
        var valueIdentity = CliTypeIdentity.Named(assembly, "Tests", "Value", true);
        var arrayIdentity = CliTypeIdentity.SzArray(valueIdentity);
        var objectLayouts = ImmutableDictionary<EntityKey, ObjectLayout>.Empty
            .Add(stringKey, new ObjectLayout(1, 12, []))
            .Add(exceptionKey, new ObjectLayout(2, 24, []))
            .Add(arrayKey, new ObjectLayout(3, 16, []))
            .Add(valueKey, new ObjectLayout(4, 8, []));
        var types = new ManagedTypeLayouts(
            objectLayouts,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(valueIdentity, new ObjectLayout(4, 8, [])),
            objectLayouts.ToImmutableDictionary(
                pair => pair.Value.TypeId.ToString(CultureInfo.InvariantCulture),
                pair => pair.Value),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(arrayIdentity, new ObjectLayout(5, 16, [])),
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        types.ValueLayoutState.Values.Add(
            valueIdentity,
            new ValueLayout(valueIdentity, 4, 4, []));
        var program = EmptyProgram() with
        {
            StringLiterals = ["hello"],
            ImplicitExceptions = [ManagedExceptionKind.OutOfMemory],
        };
        var state = new ManagedStaticDataBuildState();
        var metadata = new TypeRepository([
            stringDefinition,
            exceptionDefinition,
            arrayDefinition,
            valueDefinition,
        ]);
        var fields = new FieldRepository([]);
        var finder = new TypeFinder([
            stringDefinition,
            exceptionDefinition,
            arrayDefinition,
            valueDefinition,
            Definition(new EntityKey(assembly, 99), "System.Exception", false),
        ]);
        var bitmaps = As<IStaticReferenceBitmapBuilder>(new StaticReferenceBitmapBuilder());
        var target = types.Target;
        var valueStorageIdentities = As<ITypeIdentityResolver>(
            new TypeIdentities(assembly, "Tests.Value", valueKey));
        var exceptionTypeNames = As<IExceptionTypeNameResolver>(
            new ExceptionTypeNameResolver());
        var typeDefinitions = new TypeDefinitions([
            stringDefinition,
            exceptionDefinition,
            arrayDefinition,
            valueDefinition,
        ]);
        var typeIdentities = new MappingIdentities(
            new Dictionary<EntityKey, CliTypeIdentity>
            {
                [stringKey] = CliTypeIdentity.Named(assembly, "System", "String", false),
                [exceptionKey] = CliTypeIdentity.Named(
                    assembly, "System", "OutOfMemoryException", false),
                [arrayKey] = CliTypeIdentity.Named(assembly, "System", "Array", false),
                [valueKey] = valueIdentity,
            });
        var objectLayoutResolver = new ObjectLayoutResolver(typeDefinitions, types);

        BuildStaticFields(As<IStaticFieldStorageBuilder>(new StaticFieldStorageBuilder(
            metadata,
            fields,
            program,
            types,
            state)));
        BuildTypeDescriptors(As<ITypeDescriptorBuilder>(new TypeDescriptorBuilder(
            typeIdentities,
            typeDefinitions,
            new BaseTypes(),
            objectLayoutResolver,
            program,
            types,
            target,
            state,
            bitmaps)));
        BuildConstructedTypeDescriptors(As<IConstructedTypeDescriptorBuilder>(
            new ConstructedTypeDescriptorBuilder(
            finder,
            typeDefinitions,
            new BaseTypes(),
            objectLayoutResolver,
            types,
            target,
            state,
            bitmaps)));
        BuildValueTypeDescriptors(As<IValueTypeDescriptorBuilder>(
            new ValueTypeDescriptorBuilder(
            metadata,
            valueStorageIdentities,
            types,
            target,
            state,
            bitmaps)));
        var enumTypes = new[]
        {
            CliTypeIdentity.Primitive("i1", CliValueKind.I4),
            CliTypeIdentity.Primitive("i2", CliValueKind.I4),
            CliTypeIdentity.Primitive("char", CliValueKind.I4),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("u8", CliValueKind.I8),
        };
        foreach (var (underlying, index) in enumTypes.Select((type, index) =>
                     (type, index)))
        {
            var enumKey = new EntityKey(assembly, 5 + index);
            var enumIdentity = CliTypeIdentity.Named(assembly, "Tests", $"Enum{index}", true, underlying.StackKind);
            state.PendingEnumMetadata.Add(enumIdentity, new PendingEnumMetadata(
                enumKey,
                enumIdentity,
                99 + index,
                underlying,
                false,
                false,
                [new EnumMemberModel($"Enum{index}", ulong.MaxValue)],
                EnumMetadataPayload.Names | EnumMetadataPayload.Values |
                EnumMetadataPayload.RuntimeDescriptor));
        }
        BuildStrings(As<IStringDataBuilder>(
            new StringDataBuilder(finder, program, types, target, state)));
        BuildExceptions(As<IExceptionObjectBuilder>(
            new ExceptionObjectBuilder(
                finder,
                fields,
                program,
                types,
                target,
                state,
                exceptionTypeNames)));

        Assert.Contains("hello", state.Strings.Keys);
        Assert.DoesNotContain("FFFFFFFFFFFFFFFF", state.Strings.Keys);
        Assert.Contains(ManagedExceptionKind.OutOfMemory, state.ExceptionObjects.Keys);
        var exceptionAddress = state.ExceptionObjects[ManagedExceptionKind.OutOfMemory];
        Assert.Equal(
            24,
            state.Segments.Single(segment => segment.Address == exceptionAddress).Data.Length);
        Assert.NotEmpty(state.TypeDescriptors);
        Assert.NotEmpty(state.ConstructedTypeDescriptors);
        Assert.NotEmpty(state.ValueTypeDescriptors);
        Assert.NotEmpty(state.Segments);
    }

    [Fact]
    public void OpenGenericBaseDoesNotRequireAConcreteObjectLayout()
    {
        var assembly = new AssemblyIdentity("OpenGenericBaseDescriptorTests");
        var baseKey = new EntityKey(assembly, 1);
        var derivedKey = new EntityKey(assembly, 2);
        var baseDefinition = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Base`1",
            isValueType: false);
        var derivedDefinition = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Derived`1",
            isValueType: false);
        var openBase = CliTypeIdentity.GenericInstantiation(
            baseDefinition,
            [CliTypeIdentity.GenericParameter(method: false, 0)]);
        var definitions = new[]
        {
            Definition(baseKey, "Tests.Base`1", false) with { GenericArity = 1 },
            Definition(derivedKey, "Tests.Derived`1", false) with { GenericArity = 1 },
        };
        var layout = new ObjectLayout(1, 8, []);
        var types = EmptyLayouts(WasmTargetLayout.Wasm32) with
        {
            Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(
                derivedKey,
                layout),
            ObjectIdentities = ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(
                derivedDefinition,
                layout),
        };
        var typeDefinitions = new TypeDefinitions(definitions);
        var state = new ManagedStaticDataBuildState();
        var builder = new TypeDescriptorBuilder(
            new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
            {
                [derivedKey] = derivedDefinition,
            }),
            typeDefinitions,
            new MappingIdentityBases(new Dictionary<CliTypeIdentity, CliTypeIdentity?>
            {
                [derivedDefinition] = openBase,
            }),
            new ObjectLayoutResolver(typeDefinitions, types),
            EmptyProgram(),
            types,
            types.Target,
            state,
            new StaticReferenceBitmapBuilder());

        builder.Build();

        var descriptor = Assert.Single(state.TypeDescriptors);
        Assert.Equal(derivedKey, descriptor.Type);
        Assert.Equal(0, descriptor.BaseTypeId);
    }

    [Fact]
    public void ConstructedDescriptorBuilderCoversArrayBaseAndMissingLayouts()
    {
        var assembly = new AssemblyIdentity("ConstructedDescriptorTests");
        var arrayKey = new EntityKey(assembly, 1);
        var fallbackKey = new EntityKey(assembly, 2);
        var argument = CliTypeIdentity.Named(assembly, "Tests", "Argument", false);
        var genericDefinition = CliTypeIdentity.Named(
            assembly, "Tests", "Generic`1", false);
        var baseDefinition = CliTypeIdentity.Named(
            assembly, "Tests", "Base`1", false);
        var baseConstructed = CliTypeIdentity.GenericInstantiation(
            baseDefinition, [argument]);
        var constructedBase = CliTypeIdentity.GenericInstantiation(
            genericDefinition, [argument]);
        var constructedNamedBase = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Tests", "NamedGeneric`1", false),
            [argument]);
        var constructedFallbackBase = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Tests", "FallbackGeneric`1", false),
            [argument]);
        var constructedNoBase = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Tests", "PlainGeneric`1", false),
            [argument]);
        var array = CliTypeIdentity.SzArray(argument);
        var definitions = new[]
        {
            Definition(arrayKey, "System.Array", false),
            Definition(fallbackKey, "Tests.FallbackBase", false),
            Definition(new EntityKey(assembly, 3), "Tests.Generic`1", false),
            Definition(new EntityKey(assembly, 4), "Tests.Base`1", false),
            Definition(new EntityKey(assembly, 5), "Tests.NamedGeneric`1", false),
            Definition(new EntityKey(assembly, 6), "Tests.FallbackGeneric`1", false),
            Definition(new EntityKey(assembly, 7), "Tests.PlainGeneric`1", false),
            Definition(new EntityKey(assembly, 8), "Tests.MissingBase", false),
        };
        var objectLayouts = ImmutableDictionary<EntityKey, ObjectLayout>.Empty
            .Add(arrayKey, new ObjectLayout(1, 16, []))
            .Add(fallbackKey, new ObjectLayout(2, 12, []));
        var constructedLayouts = ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
            .Add(baseConstructed, new ObjectLayout(3, 12, []))
            .Add(constructedBase, new ObjectLayout(4, 16, [0]))
            .Add(constructedNamedBase, new ObjectLayout(5, 16, []))
            .Add(constructedFallbackBase, new ObjectLayout(6, 16, []))
            .Add(constructedNoBase, new ObjectLayout(7, 16, []))
            .Add(array, new ObjectLayout(8, 16, []));
        var namedLayouts = ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
            .Add(CliTypeIdentity.Named(assembly, "Tests", "NamedBase", false),
                new ObjectLayout(9, 12, []));
        var types = new ManagedTypeLayouts(
            objectLayouts,
            namedLayouts,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            constructedLayouts,
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var namedBase = namedLayouts.Keys.Single();
        var identityBases = new MappingIdentityBases(
            new Dictionary<CliTypeIdentity, CliTypeIdentity?>
            {
                [constructedBase] = baseConstructed,
                [constructedNamedBase] = namedBase,
                [constructedFallbackBase] = CliTypeIdentity.Named(
                    assembly, "Tests", "FallbackBase", false),
                [constructedNoBase] = null,
            });
        var typeDefinitions = new TypeDefinitions(definitions);
        var builder = new ConstructedTypeDescriptorBuilder(
            new TypeFinder(definitions),
            typeDefinitions,
            identityBases,
            new ObjectLayoutResolver(typeDefinitions, types),
            types,
            types.Target,
            new ManagedStaticDataBuildState(),
            new StaticReferenceBitmapBuilder());

        builder.Build();

        Assert.Equal(6, types.ConstructedObjects.Count);

        var missingArrayTypes = types with
        {
            Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ConstructedObjects = ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(array, new ObjectLayout(1, 8, [])),
        };
        var missingArray = new ConstructedTypeDescriptorBuilder(
            new TypeFinder(definitions),
            typeDefinitions,
            identityBases,
            new ObjectLayoutResolver(typeDefinitions, missingArrayTypes),
            missingArrayTypes,
            types.Target,
            new ManagedStaticDataBuildState(),
            new StaticReferenceBitmapBuilder());
        Assert.Throws<CompilerException>(missingArray.Build);

        var missingBaseTypes = types with
        {
            ConstructedObjects = ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(constructedNoBase, new ObjectLayout(1, 8, [])),
        };
        var missingBase = new ConstructedTypeDescriptorBuilder(
            new TypeFinder(definitions),
            typeDefinitions,
            new MappingIdentityBases(
                new Dictionary<CliTypeIdentity, CliTypeIdentity?>
                {
                    [constructedNoBase] = CliTypeIdentity.Named(
                        assembly, "Tests", "MissingBase", false),
                }),
            new ObjectLayoutResolver(typeDefinitions, missingBaseTypes),
            missingBaseTypes,
            types.Target,
            new ManagedStaticDataBuildState(),
            new StaticReferenceBitmapBuilder());
        Assert.Throws<CompilerException>(missingBase.Build);
    }

    [Fact]
    public void ConstructedTypeDescriptorCarriesDemandedDelegateInvokeIdentity()
    {
        var assembly = new AssemblyIdentity("ConstructedDelegateDescriptorTests");
        var typeKey = new EntityKey(assembly, 1);
        var invokeKey = new EntityKey(assembly, 2);
        var generic = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Callback`1",
            isValueType: false);
        var argument = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var constructed = CliTypeIdentity.GenericInstantiation(generic, [argument]);
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var invokeDefinition = new MethodDefinitionModel(
            invokeKey,
            typeKey,
            "Invoke",
            IsStatic: false,
            MethodSignatureModel.Create(parameter, parameter),
            RelativeVirtualAddress: 0);
        var invoke = new MethodInstanceModel(
            invokeDefinition,
            constructed,
            [],
            invokeDefinition.Signature.Substitute([argument]));
        var definition = new TypeDefinitionModel(
            typeKey,
            "Tests",
            "Callback`1",
            IsValueType: false,
            [],
            [invokeKey])
        {
            GenericArity = 1,
            GenericParameterNames = ["T"],
        };
        var intDefinition = Definition(
            new EntityKey(assembly, 3),
            "System.Int32",
            true);
        var layout = new ObjectLayout(1, 8, []);
        var types = EmptyLayouts(WasmTargetLayout.Wasm32) with
        {
            ConstructedObjects =
                ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(
                    constructed,
                    layout),
        };
        var definitions = new TypeDefinitions([definition, intDefinition]);
        var state = new ManagedStaticDataBuildState();

        new ConstructedTypeDescriptorBuilder(
            new TypeFinder([definition]),
            definitions,
            new BaseTypes(),
            new ObjectLayoutResolver(definitions, types),
            types,
            types.Target,
            state,
            new StaticReferenceBitmapBuilder(),
            EmptyAssignableTypeMetadataBuilder.Instance,
            requiresTypeFacts: true,
            delegateInvokeDescriptors:
                ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    constructed.CanonicalName,
                    invoke)).Build();

        Assert.Equal(
            invoke.CanonicalName,
            Assert.Single(state.PendingTypeFacts).DelegateInvokeDescriptor);

        var stateWithoutInvoke = new ManagedStaticDataBuildState();
        new ConstructedTypeDescriptorBuilder(
            new TypeFinder([definition]),
            definitions,
            new BaseTypes(),
            new ObjectLayoutResolver(definitions, types),
            types,
            types.Target,
            stateWithoutInvoke,
            new StaticReferenceBitmapBuilder(),
            EmptyAssignableTypeMetadataBuilder.Instance,
            requiresTypeFacts: true).Build();
        var factsWithoutInvoke = Assert.Single(stateWithoutInvoke.PendingTypeFacts);
        Assert.Null(factsWithoutInvoke.DelegateInvokeDescriptor);
    }

    [Fact]
    public void StaticFieldActorsCoverConstructedAndMissingStorageBranches()
    {
        var assembly = new AssemblyIdentity("StaticFieldBranchTests");
        var typeKey = new EntityKey(assembly, 1);
        var genericTypeKey = new EntityKey(assembly, 2);
        var staticKey = new EntityKey(assembly, 3);
        var genericStaticKey = new EntityKey(assembly, 4);
        var value = CliTypeIdentity.Named(assembly, "Tests", "Value", true);
        var field = new FieldDefinitionModel(staticKey, typeKey, "Value", value, true);
        var genericField = new FieldDefinitionModel(
            genericStaticKey,
            genericTypeKey,
            "GenericValue",
            value,
            true);
        var definitions = new[]
        {
            new TypeDefinitionModel(typeKey, "Tests", "Holder", false, [staticKey], []),
            new TypeDefinitionModel(genericTypeKey, "Tests", "Generic`1", false,
                [genericStaticKey], []) { GenericArity = 1 },
        };
        var fields = new FieldRepository([field, genericField]);
        var valueLayout = new ValueLayout(value, 4, 4, [0]);
        var types = EmptyLayouts(WasmTargetLayout.For(WasmTarget.Wasm32));
        types.ValueLayoutState.Values.Add(value, valueLayout);
        var constructed = new FieldInstanceModel(genericField, value, value);
        var program = EmptyProgram() with
        {
            Fields = [staticKey, genericStaticKey],
            ConstructedFields = new Dictionary<string, FieldInstanceModel>
            {
                [constructed.CanonicalName] = constructed,
            }.ToImmutableDictionary(StringComparer.Ordinal),
        };
        var state = new ManagedStaticDataBuildState();
        new StaticFieldStorageBuilder(
            new TypeRepository(definitions), fields, program, types, state).Build();
        Assert.Contains(staticKey, state.StaticFields.Keys);
        Assert.Contains(constructed.CanonicalName, state.ConstructedStaticFields.Keys);
        Assert.Equal(2, state.StaticRoots.Count);

        var resolved = new RecordingValueLayoutResolver();
        new StaticFieldValueLayoutResolver(fields, program, resolved).Resolve();
        Assert.Contains("[StaticFieldBranchTests]Tests.Value", resolved.Types
            .Select(type => type.CanonicalName));

        var missingTypes = EmptyLayouts(WasmTargetLayout.For(WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => new StaticFieldStorageBuilder(
            new TypeRepository(definitions), fields,
            EmptyProgram() with { Fields = [staticKey] },
            missingTypes,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void TypeDescriptorsRecordBaseTypesAndInterfaceTraits()
    {
        var assembly = new AssemblyIdentity("DescriptorTraitsTests");
        var baseKey = new EntityKey(assembly, 1);
        var derivedKey = new EntityKey(assembly, 2);
        var interfaceKey = new EntityKey(assembly, 3);
        var baseIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "Base", isValueType: false);
        var derivedIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "Derived", isValueType: false);
        var interfaceIdentity = CliTypeIdentity.Named(
            assembly, "Tests", "IMarker", isValueType: false);
        var definitions = new[]
        {
            Definition(baseKey, "Tests.Base", false),
            Definition(derivedKey, "Tests.Derived", false),
            Definition(interfaceKey, "Tests.IMarker", false) with { IsInterface = true },
        };
        var objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty
            .Add(baseKey, new ObjectLayout(1, 8, []))
            .Add(derivedKey, new ObjectLayout(2, 8, []))
            .Add(interfaceKey, new ObjectLayout(3, 8, []));
        var types = new ManagedTypeLayouts(
            objects,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(baseIdentity, objects[baseKey])
                .Add(derivedIdentity, objects[derivedKey])
                .Add(interfaceIdentity, objects[interfaceKey]),
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var typeDefinitions = new TypeDefinitions(definitions);
        var state = new ManagedStaticDataBuildState();
        new TypeDescriptorBuilder(
            new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
            {
                [baseKey] = baseIdentity,
                [derivedKey] = derivedIdentity,
                [interfaceKey] = interfaceIdentity,
            }),
            typeDefinitions,
            new MappingIdentityBases(new Dictionary<CliTypeIdentity, CliTypeIdentity?>
            {
                [baseIdentity] = null,
                [derivedIdentity] = baseIdentity,
                [interfaceIdentity] = null,
            }),
            new ObjectLayoutResolver(typeDefinitions, types),
            EmptyProgram(),
            types,
            types.Target,
            state,
            new StaticReferenceBitmapBuilder()).Build();

        Assert.Equal(1, state.TypeDescriptors.Single(item => item.Type == derivedKey).BaseTypeId);
        Assert.True(state.TypeDescriptors.Single(item => item.Type == interfaceKey).IsInterface);
        Assert.False(state.TypeDescriptors.Single(item => item.Type == baseKey).IsInterface);


    }

    [Fact]
    public void NamedTypeDescriptorCarriesDemandedDelegateInvokeIdentity()
    {
        var assembly = new AssemblyIdentity("NamedDelegateDescriptorTests");
        var typeKey = new EntityKey(assembly, 1);
        var invokeKey = new EntityKey(assembly, 2);
        var identity = CliTypeIdentity.Named(
            assembly,
            "Tests",
            "Callback",
            isValueType: false);
        var invokeDefinition = new MethodDefinitionModel(
            invokeKey,
            typeKey,
            "Invoke",
            IsStatic: false,
            MethodSignatureModel.Create(CliValueKind.I4),
            RelativeVirtualAddress: 0);
        var invoke = new MethodInstanceModel(
            invokeDefinition,
            identity,
            [],
            invokeDefinition.Signature);
        var definition = new TypeDefinitionModel(
            typeKey,
            "Tests",
            "Callback",
            IsValueType: false,
            [],
            [invokeKey]);
        var layout = new ObjectLayout(1, 8, []);
        var types = EmptyLayouts(WasmTargetLayout.Wasm32) with
        {
            Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(
                typeKey,
                layout),
            ObjectIdentities =
                ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(
                    identity,
                    layout),
        };
        var definitions = new TypeDefinitions([definition]);
        var state = new ManagedStaticDataBuildState();
        var program = EmptyProgram() with
        {
            RequiresTypeFacts = true,
            DelegateInvokeDescriptors =
                ImmutableDictionary<string, MethodInstanceModel>.Empty.Add(
                    identity.CanonicalName,
                    invoke),
        };

        new TypeDescriptorBuilder(
            new MappingIdentities(
                new Dictionary<EntityKey, CliTypeIdentity> { [typeKey] = identity }),
            definitions,
            new BaseTypes(),
            new ObjectLayoutResolver(definitions, types),
            program,
            types,
            types.Target,
            state,
            new StaticReferenceBitmapBuilder()).Build();

        Assert.Equal(
            invoke.CanonicalName,
            Assert.Single(state.PendingTypeFacts).DelegateInvokeDescriptor);

        var stateWithoutInvoke = new ManagedStaticDataBuildState();
        new TypeDescriptorBuilder(
            new MappingIdentities(
                new Dictionary<EntityKey, CliTypeIdentity> { [typeKey] = identity }),
            definitions,
            new BaseTypes(),
            new ObjectLayoutResolver(definitions, types),
            program with
            {
                DelegateInvokeDescriptors =
                    ImmutableDictionary<string, MethodInstanceModel>.Empty,
            },
            types,
            types.Target,
            stateWithoutInvoke,
            new StaticReferenceBitmapBuilder(),
            EmptyAssignableTypeMetadataBuilder.Instance).Build();
        var factsWithoutInvoke = Assert.Single(stateWithoutInvoke.PendingTypeFacts);
        Assert.Null(factsWithoutInvoke.DelegateInvokeDescriptor);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.None)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.None)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Names)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Names)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Names | EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Names | EnumMetadataPayload.Values)]
    [InlineData(WasmTarget.Wasm32, EnumMetadataPayload.Names | EnumMetadataPayload.Values | EnumMetadataPayload.RuntimeDescriptor)]
    [InlineData(WasmTarget.Wasm64, EnumMetadataPayload.Names | EnumMetadataPayload.Values | EnumMetadataPayload.RuntimeDescriptor)]
    public void EnumStringsFollowExplicitPayloadDemand(WasmTarget wasm, EnumMetadataPayload payload)
    {
        var target = WasmTargetLayout.For(wasm);
        var assembly = new AssemblyIdentity("SelectiveEnumStrings");
        var stringKey = new EntityKey(assembly, 1);
        var enumKey = new EntityKey(assembly, 2);
        var types = EmptyLayouts(target) with
        {
            Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty
                .Add(stringKey, new ObjectLayout(1, target.ObjectHeaderSize + 4, [])),
        };
        var state = new ManagedStaticDataBuildState();
        var enumIdentity = CliTypeIdentity.Named(assembly, "Tests", "Colors", true, CliValueKind.I4);
        state.PendingEnumMetadata.Add(enumIdentity, new PendingEnumMetadata(
            enumKey, enumIdentity, 2, CliTypeIdentity.Primitive("u4", CliValueKind.I4), true, false,
            [new("None", 0), new("Red", 1), new("Blue", 2)], payload));
        var builder = As<IStringDataBuilder>(new StringDataBuilder(
            new TypeFinder([Definition(stringKey, "System.String", false)]),
            EmptyProgram(), types, target, state));

        builder.Build();

        string[] expected = (payload & EnumMetadataPayload.Names) == 0
            ? []
            : ["None", "Red", "Blue"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), state.Strings.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(expected.Length, state.Segments.Count);
        Assert.All(state.Strings.Values, layout =>
            Assert.Equal(0, layout.Address % target.ObjectReferenceAlignment));
    }

    [Fact]
    public void StringAndTypeDescriptorBuildersCoverMissingAndFinalizerBranches()
    {
        var assembly = new AssemblyIdentity("DescriptorBranchTests");
        var typeKey = new EntityKey(assembly, 1);
        var definition = Definition(typeKey, "Tests.Node", false);
        var typeIdentity = CliTypeIdentity.Named(assembly, "Tests", "Node", false);
        var finalizerDefinition = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            typeKey,
            "Finalize",
            false,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var finalizer = new MethodInstanceModel(
            finalizerDefinition,
            typeIdentity,
            [],
            finalizerDefinition.Signature);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty
                .Add(typeKey, new ObjectLayout(1, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var program = EmptyProgram() with
        {
            Finalizers = ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty
                .Add(typeIdentity, finalizer),
        };
        var staticState = new ManagedStaticDataBuildState();
        var definitions = new TypeDefinitions([definition]);
        new TypeDescriptorBuilder(
            new MappingIdentities(new Dictionary<EntityKey, CliTypeIdentity>
            {
                [typeKey] = typeIdentity,
            }),
            definitions,
            new BaseTypes(),
            new ObjectLayoutResolver(definitions, types),
            program,
            types,
            types.Target,
            staticState,
            new StaticReferenceBitmapBuilder()).Build();
        Assert.Equal(typeKey, Assert.Single(staticState.TypeDescriptors).Type);
        Assert.Equal(finalizer, Assert.Single(staticState.TypeDescriptors).Finalizer);

        Assert.Throws<CompilerException>(() => new StringDataBuilder(
            new TypeFinder([definition, Definition(
                new EntityKey(assembly, 2), "System.String", false)]),
            EmptyProgram() with { StringLiterals = ["missing"] },
            types with { Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty },
            types.Target,
            new ManagedStaticDataBuildState()).Build());
        var exceptionDefinition = Definition(
            new EntityKey(assembly, 3),
            "System.OutOfMemoryException",
            false);
        Assert.Throws<CompilerException>(() => new ExceptionObjectBuilder(
            new TypeFinder([exceptionDefinition]),
            new FieldRepository([]),
            EmptyProgram() with
            {
                ImplicitExceptions = [ManagedExceptionKind.OutOfMemory],
            },
            types with { Objects = ImmutableDictionary<EntityKey, ObjectLayout>.Empty },
            types.Target,
            new ManagedStaticDataBuildState(),
            new ExceptionTypeNameResolver()).Build());
    }

    [Fact]
    public void ValueTypeDescriptorBuilderCoversConstructedValuesAndMissingLayouts()
    {
        var assembly = new AssemblyIdentity("ValueDescriptorBranchTests");
        var typeKey = new EntityKey(assembly, 1);
        var type = CliTypeIdentity.Named(assembly, "Tests", "Value", true);
        var constructed = CliTypeIdentity.GenericInstantiation(type, [type]);
        var boxed = new ObjectLayout(1, 8, []);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(constructed, boxed),
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        types.ValueLayoutState.Values.Add(
            constructed,
            new ValueLayout(constructed, 4, 4, []));
        var state = new ManagedStaticDataBuildState();
        new ValueTypeDescriptorBuilder(
            new TypeRepository([]),
            new UnusedTypeIdentities(),
            types,
            types.Target,
            state,
            new StaticReferenceBitmapBuilder()).Build();
        Assert.Equal(constructed, Assert.Single(state.ValueTypeDescriptors).Type);

        Assert.Throws<CompilerException>(() => new ValueTypeDescriptorBuilder(
            new TypeRepository([]),
            new UnusedTypeIdentities(),
            types with
            {
                ValueLayoutState = new ManagedValueLayoutState(),
            },
            types.Target,
            new ManagedStaticDataBuildState(),
            new StaticReferenceBitmapBuilder()).Build());
    }

    private static byte[] BuildBitmap(
        IStaticReferenceBitmapBuilder builder,
        ImmutableArray<int> offsets,
        int bitCount,
        int wordSize) => builder.Build(offsets, bitCount, wordSize);

    private static CliTypeIdentity ResolveTypeIdentity(
        ITypeIdentityResolver resolver,
        EntityKey key) => resolver.GetTypeIdentity(key);

    private static string ResolveExceptionName(
        IExceptionTypeNameResolver resolver,
        ManagedExceptionKind kind) => resolver.Resolve(kind);

    private static ObjectLayout BuildObject(
        IManagedObjectLayoutBuilder builder,
        EntityKey key) => builder.Build(key);

    private static void BuildReachableObjects(IReachableObjectLayoutBuilder builder) =>
        builder.Build();

    private static void ResolveStaticFieldValues(IStaticFieldValueLayoutResolver resolver) =>
        resolver.Resolve();

    private static void ResolveMethodValues(IMethodValueLayoutResolver resolver) =>
        resolver.Resolve();

    private static void BuildImplicitObjects(IImplicitObjectLayoutBuilder builder) =>
        builder.Build();

    private static void ResolveValueTypes(IValueTypeLayoutResolver resolver) =>
        resolver.Resolve();

    private static ManagedDelegateFieldOffsets ResolveDelegateOffsets(
        IDelegateFieldOffsetResolver resolver) => resolver.Resolve();

    private static void ThrowsNull(Func<object> action) =>
        Assert.Throws<ArgumentNullException>(() => action());

    private static ManagedTypeLayouts EmptyLayouts(WasmTargetLayout target) =>
        new(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            target,
            -1,
            -1,
            -1,
            -1);

    private static ManagedLayoutSnapshot EmptySnapshot(WasmTargetLayout target)
    {
        var types = EmptyLayouts(target);
        return new ManagedLayoutSnapshot(
            types,
            new ManagedStaticData(16, [], [], [], [], [], [], [], [], []));
    }

    private static void BuildStaticFields(IStaticFieldStorageBuilder builder) => builder.Build();
    private static void BuildTypeDescriptors(ITypeDescriptorBuilder builder) => builder.Build();
    private static void BuildConstructedTypeDescriptors(
        IConstructedTypeDescriptorBuilder builder) => builder.Build();
    private static void BuildValueTypeDescriptors(IValueTypeDescriptorBuilder builder) =>
        builder.Build();
    private static void BuildStrings(IStringDataBuilder builder) => builder.Build();
    private static void BuildExceptions(IExceptionObjectBuilder builder) => builder.Build();

    private static TContract As<TContract>(object actor) where TContract : class =>
        (TContract)actor;

    // Each check's exception is a new one; only the OutOfMemoryException thrown
    // when not even that fits is frozen in the image, as
    // new OutOfMemoryException(message: null) makes it, and its reference fields
    // are roots, so what a program stores in it, as Data, survives collections.
    [Fact]
    public void OnlyOutOfMemoryIsFrozenWithItsHResultAndRootedReferences()
    {
        var assembly = new AssemblyIdentity("Exceptions");
        var hResult = new FieldDefinitionModel(
            new EntityKey(assembly, 7), new EntityKey(assembly, 6), "_hResult",
            CliTypeIdentity.Primitive("i4", CliValueKind.I4), false);
        var exception = Definition(new EntityKey(assembly, 6), "System.Exception", false) with { Fields = [hResult.Key] };
        var nullReference = Definition(new EntityKey(assembly, 4), "System.NullReferenceException", false);
        var outOfMemory = Definition(new EntityKey(assembly, 5), "System.OutOfMemoryException", false);
        var values = new ManagedValueLayoutState();
        values.Fields.Add(hResult.Key, new FieldLayout(28));
        var layouts = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty
                .Add(nullReference.Key, new ObjectLayout(11, 40, [8, 16]))
                .Add(outOfMemory.Key, new ObjectLayout(12, 40, [8, 16, 24])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            values,
            WasmTargetLayout.Wasm32,
            -1,
            -1,
            -1,
            -1);
        var state = new ManagedStaticDataBuildState { Cursor = 17 };

        new ExceptionObjectBuilder(
            new TypeFinder([exception, nullReference, outOfMemory]),
            new FieldRepository([hResult]),
            EmptyProgram() with { ImplicitExceptions = [ManagedExceptionKind.NullReference, ManagedExceptionKind.OutOfMemory] },
            layouts,
            WasmTargetLayout.Wasm32,
            state,
            new ExceptionTypeNameResolver()).Build();

        var (kind, address) = Assert.Single(state.ExceptionObjects);
        Assert.Equal(ManagedExceptionKind.OutOfMemory, kind);
        Assert.Equal(0, address % WasmTargetLayout.Wasm32.ObjectReferenceAlignment);
        Assert.Equal([address + 8, address + 16, address + 24], state.StaticRoots);
        var segment = Assert.Single(state.Segments);
        Assert.Equal(address, segment.Address);
        Assert.Equal(12, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(segment.Data.AsSpan()));
        Assert.Equal(RuntimeExceptionGetters.OutOfMemoryHResult,
            System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(segment.Data.AsSpan()[28..]));
        Assert.All(segment.Data.AsSpan()[4..28].ToArray(), static value => Assert.Equal(0, value));
        Assert.Equal(address + 40, state.Cursor);
    }

    [Fact]
    public void NoOutOfMemoryLeavesNoExceptionObject()
    {
        var state = new ManagedStaticDataBuildState { Cursor = 17 };

        new ExceptionObjectBuilder(
            new TypeFinder([]),
            new FieldRepository([]),
            EmptyProgram() with { ImplicitExceptions = [ManagedExceptionKind.NullReference] },
            new ManagedTypeLayouts([], [], [], [], new(), WasmTargetLayout.Wasm32, -1, -1, -1, -1),
            WasmTargetLayout.Wasm32,
            state,
            new ExceptionTypeNameResolver()).Build();

        Assert.Empty(state.ExceptionObjects);
        Assert.Empty(state.Segments);
        Assert.Equal(17, state.Cursor);
    }

    private static TypeDefinitionModel Definition(EntityKey key, string name, bool valueType) =>
        new(key, name.Contains('.') ? name[..name.LastIndexOf('.')].TrimEnd('.') : "",
            name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name,
            valueType,
            [],
            []);

    private static ReachableProgram EmptyProgram()
    {
        var assembly = new AssemblyIdentity("LayoutProgram");
        var type = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        return new ReachableProgram(method, [], [], [], [], [], [], [], [], [], []);
    }

    private static ManagedMethodBody MethodWithLocalValue(params CilInstruction[] instructions)
    {
        var assembly = new AssemblyIdentity("CompilerPhaseMethodTests");
        var type = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            type,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        var body = new CilMethodBody(
            method,
            0,
            [],
            instructions.Length == 0
                ? [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())]
                : [.. instructions,
                    new CilInstruction(instructions[^1].NextOffset,
                        instructions[^1].NextOffset + 1, CilOperation.Pop, new CilOperand.None()),
                    new CilInstruction(instructions[^1].NextOffset + 1,
                        instructions[^1].NextOffset + 2, CilOperation.Return, new CilOperand.None())])
        {
            LocalSignatureTypes = [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
            MethodInstance = new MethodInstanceModel(
                method,
                CliTypeIdentity.Named(assembly, "", "Unit", false),
                [],
                method.Signature),
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var validated = new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []));
        return new ManagedMethodBody(body.MethodInstance, validated);
    }

    private sealed class FixedValueLayoutResolver : IValueLayoutResolver
    {
        public ValueLayout Resolve(CliTypeIdentity type) =>
            new(type, 4, 4, []);

        public ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition)
            => Resolve(type);
    }

    private sealed class RecordingValueLayoutResolver : IValueLayoutResolver
    {
        public List<CliTypeIdentity> Types { get; } = [];

        public List<TypeDefinitionModel> Definitions { get; } = [];

        public ValueLayout Resolve(CliTypeIdentity type)
        {
            Types.Add(type);
            return new ValueLayout(type, 4, 4, []);
        }

        public ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition)
        {
            Definitions.Add(definition);
            return Resolve(type);
        }
    }

    private sealed class RecordingObjectLayoutBuilder : IManagedObjectLayoutBuilder
    {
        public List<EntityKey> EntityTypes { get; } = [];

        public List<CliTypeIdentity> ConstructedTypes { get; } = [];

        public ObjectLayout Build(EntityKey type)
        {
            EntityTypes.Add(type);
            return new ObjectLayout(EntityTypes.Count, 8, []);
        }

        public ObjectLayout Build(CliTypeIdentity type)
        {
            ConstructedTypes.Add(type);
            return new ObjectLayout(ConstructedTypes.Count, 8, []);
        }
    }

    private sealed class RecordingBuildPhase :
        IReachableObjectLayoutBuilder,
        IImplicitObjectLayoutBuilder
    {
        public int Calls { get; private set; }

        public void Build() => Calls++;
    }

    private sealed class RecordingResolvePhase :
        IStaticFieldValueLayoutResolver,
        IMethodValueLayoutResolver,
        IValueTypeLayoutResolver
    {
        public int Calls { get; private set; }

        public void Resolve() => Calls++;
    }

    private sealed class RecordingDelegateOffsetPhase : IDelegateFieldOffsetResolver
    {
        public ManagedDelegateFieldOffsets Resolve() =>
            new(-1, -1, -1, -1);
    }

    private sealed class RecordingStaticBuildPhase :
        IStaticFieldStorageBuilder,
        ITypeDescriptorBuilder,
        IConstructedTypeDescriptorBuilder,
        IValueTypeDescriptorBuilder,
        IRuntimeGenericArgumentMetadataBuilder,
        IMemberDescriptorDataBuilder,
        IStringDataBuilder,
        IExceptionObjectBuilder,
        ITypeObjectDataBuilder,
        IEnumMetadataCollector,
        IEnumMetadataBuilder
    {
        public int Calls { get; private set; }

        public void Build() => Calls++;
        public void Collect() => Calls++;
    }

    private sealed class TypeRepository(IEnumerable<TypeDefinitionModel> definitions) :
        ITypeRepository
    {
        private readonly Dictionary<EntityKey, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.Key);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _definitions[key];
    }

    private sealed class FieldRepository(IEnumerable<FieldDefinitionModel> fields) :
        IFieldRepository
    {
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields =
            fields.ToDictionary(field => field.Key);

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];
    }

    private sealed class TypeFinder(IEnumerable<TypeDefinitionModel> definitions) : ITypeFinder
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName, StringComparer.Ordinal);

        public TypeDefinitionModel FindType(string fullName) => _definitions[fullName];
    }

    private sealed class MappedTypeFinder(params TypeDefinitionModel[] definitions) : ITypeFinder
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName, StringComparer.Ordinal);

        public TypeDefinitionModel FindType(string fullName) => _definitions[fullName];
    }

    private sealed class TypeDefinitions(IEnumerable<TypeDefinitionModel> definitions) :
        ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName, StringComparer.Ordinal);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            var fullName = identity.Shape == CliTypeShape.GenericInstantiation
                ? identity.ElementType!.FullName
                : identity.FullName;
            fullName ??= identity.CanonicalName switch
            {
                "primitive:i4" => "System.Int32",
                "primitive:string" => "System.String",
                "primitive:object" => "System.Object",
                _ => throw new InvalidOperationException(
                    $"Test identity '{identity.CanonicalName}' has no metadata full name."),
            };
            return _definitions[fullName];
        }
    }

    private sealed class TypeIdentities(
        AssemblyIdentity assembly,
        string fullName,
        EntityKey key) : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey requested) =>
            requested == key
                ? CliTypeIdentity.Named(assembly, "Tests", fullName["Tests.".Length..], true)
                : throw new KeyNotFoundException(requested.ToString());
    }

    private sealed class UnusedTypeIdentities : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) =>
            throw new InvalidOperationException(type.ToString());
    }

    private sealed class BaseTypes :
        IMetadataEntityBaseTypeResolver,
        IMetadataIdentityBaseTypeResolver
    {
        public EntityKey? GetBaseType(EntityKey typeKey) => null;
        public CliTypeIdentity? GetBaseType(CliTypeIdentity type) => null;
    }

    private sealed class RejectingTypeRelationshipClassifier :
        ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) =>
            throw new InvalidOperationException(
                $"Unexpected relationship classification: {candidate} -> {target}");
    }

    private sealed class MappingIdentities(
        IReadOnlyDictionary<EntityKey, CliTypeIdentity> identities) : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => identities[type];
    }

    private sealed class MappingEntityBases(
        IReadOnlyDictionary<EntityKey, EntityKey?> bases) :
        IMetadataEntityBaseTypeResolver
    {
        public EntityKey? GetBaseType(EntityKey typeKey) =>
            bases.TryGetValue(typeKey, out var value) ? value : null;
    }

    private sealed class MappingIdentityBases(
        IReadOnlyDictionary<CliTypeIdentity, CliTypeIdentity?> bases) :
        IMetadataIdentityBaseTypeResolver
    {
        public CliTypeIdentity? GetBaseType(CliTypeIdentity type) =>
            bases.TryGetValue(type, out var value) ? value : null;
    }

    private sealed class ReferenceValueLayoutResolver : IValueLayoutResolver
    {
        public ValueLayout Resolve(CliTypeIdentity type) =>
            type.IsValueType
                ? new ValueLayout(type, 4, 4, [])
                : new ValueLayout(type, 4, 4, [0]);

        public ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition)
            => Resolve(type);
    }
}
