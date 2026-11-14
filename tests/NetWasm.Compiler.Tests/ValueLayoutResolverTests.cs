using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class ValueLayoutResolverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FieldResolutionFailurePreservesMostSpecificContext(bool explicitLayout, bool existingContext)
    {
        var typeKey = new EntityKey(new("Library"), 1);
        var fieldKey = new EntityKey(typeKey.Assembly, 2);
        var definition = new TypeDefinitionModel(typeKey, "Tests", "Owner", true, [fieldKey], [])
        {
            LayoutKind = explicitLayout ? CliTypeLayoutKind.Explicit : CliTypeLayoutKind.Sequential,
        };
        var identity = CliTypeIdentity.FromDefinition(definition);
        var missing = CliTypeIdentity.Named(new("Dependency"), "Tests", "Missing", true);
        var field = new FieldDefinitionModel(fieldKey, typeKey, "Data", missing, false);
        var context = existingContext ? new CompilerDiagnosticContext(new(new("Nested"), 3)) : null;
        var failure = new CompilerException(new(DiagnosticCode.AssemblyResolution, "Missing field type")
        {
            Context = context,
        });
        var resolver = Assert.IsAssignableFrom<IValueLayoutResolver>(new ValueLayoutResolver(
            new FailingDefinitions(failure), new Fields([field]), new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32), new ManagedValueLayoutState(), new FieldLayoutFailureBinder()));

        var actual = Assert.Throws<CompilerException>(() => resolver.Resolve(identity, definition));

        if (existingContext)
        {
            Assert.Same(failure, actual.InnerException);
            Assert.Same(context, actual.Diagnostic.Context);
        }
        else
        {
            Assert.Same(failure, actual.InnerException);
            Assert.Equal(fieldKey, actual.Diagnostic.Context!.Source);
            Assert.Equal(missing.CanonicalName, actual.Diagnostic.Context.InstanceIdentity);
            Assert.Equal(identity, actual.Diagnostic.Context.DeclaringType);
        }
    }

    private sealed class FailingDefinitions(CompilerException failure) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => throw failure;
    }

    public static TheoryData<string, int, WasmTarget, bool, bool> PrimitiveResolutionOrders
    {
        get
        {
            var cases = new TheoryData<string, int, WasmTarget, bool, bool>();
            foreach (var (name, size) in new[]
            {
                ("Boolean", 1), ("SByte", 1), ("Byte", 1), ("Char", 2),
                ("Int16", 2), ("UInt16", 2), ("Int32", 4), ("UInt32", 4),
                ("Single", 4), ("Int64", 8), ("UInt64", 8), ("Double", 8),
                ("IntPtr", 0), ("UIntPtr", 0),
            })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    foreach (var primitiveFirst in new[] { false, true })
                    {
                        cases.Add(name, size, target, primitiveFirst, false);
                        cases.Add(name, size, target, primitiveFirst, true);
                    }
                }
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(PrimitiveResolutionOrders))]
    public void PrimitiveStorageDoesNotShrinkWhenMetadataIsResolvedFirst(
        string name,
        int fixedSize,
        WasmTarget target,
        bool primitiveFirst,
        bool backingField)
    {
        var assembly = new AssemblyIdentity("NetWasm.CoreLib");
        var typeKey = new EntityKey(assembly, 0x02000001);
        var fieldKey = new EntityKey(assembly, 0x04000001);
        var primitive = CliTypeIdentity.Named(assembly, "System", name, true);
        var definition = new TypeDefinitionModel(typeKey, "System", name, true,
            backingField ? [fieldKey] : [], []);
        var state = new ManagedValueLayoutState();
        var resolver = Assert.IsAssignableFrom<IValueLayoutResolver>(new ValueLayoutResolver(
            new FixedDefinition(definition),
            new Fields(backingField
                ? [new FieldDefinitionModel(fieldKey, typeKey, "_value", primitive, false)]
                : []),
            new ExplicitValueLayoutResolver(), WasmTargetLayout.For(target), state, new FieldLayoutFailureBinder()));
        var expectedSize = fixedSize == 0 ? WasmTargetLayout.For(target).AddressSize : fixedSize;
        if (primitiveFirst)
        {
            Assert.Equal(expectedSize, resolver.Resolve(primitive).Size);
        }

        var layout = resolver.Resolve(primitive, definition);

        Assert.Equal(expectedSize, layout.Size);
        Assert.Equal(expectedSize, layout.Alignment);
        Assert.Empty(layout.ReferenceOffsets);
        Assert.Equal(layout, resolver.Resolve(primitive));
        Assert.Contains(primitive, state.CompletedMetadataLayouts);
        if (backingField)
        {
            Assert.Equal(0, state.Fields[fieldKey].Offset);
            Assert.Equal(expectedSize, state.Fields[fieldKey].Size);
        }
        else
        {
            Assert.Empty(state.Fields);
        }
    }

    [Theory]
    [InlineData("i1", CliValueKind.I4, 1)]
    [InlineData("i2", CliValueKind.I4, 2)]
    [InlineData("i4", CliValueKind.I4, 4)]
    [InlineData("i8", CliValueKind.I8, 8)]
    [InlineData("f4", CliValueKind.F4, 4)]
    [InlineData("f8", CliValueKind.F8, 8)]
    public void ResolvesPrimitiveStorageWithoutMetadata(
        string name,
        CliValueKind kind,
        int size)
    {
        var resolver = CreateResolver(WasmTarget.Wasm32);

        var layout = resolver.Resolve(CliTypeIdentity.Primitive(name, kind));

        Assert.Equal(size, layout.Size);
        Assert.Equal(size, layout.Alignment);
        Assert.Empty(layout.ReferenceOffsets);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 4)]
    [InlineData(WasmTarget.Wasm64, 8)]
    public void ResolvesTargetSizedReferencesAndPointers(
        WasmTarget target,
        int size)
    {
        var resolver = CreateResolver(target);
        var value = CliTypeIdentity.Named(
            new AssemblyIdentity("Tests"),
            "Tests",
            "Value",
            isValueType: true);

        var reference = resolver.Resolve(CliTypeIdentity.SzArray(value));
        var managedAddress = resolver.Resolve(CliTypeIdentity.ManagedByReference(value));
        var pointer = resolver.Resolve(CliTypeIdentity.UnmanagedPointer(value));
        var functionPointer = CliTypeIdentity.FunctionPointer(new(0, 0, 0,
            MethodSignatureModel.Create(CliValueKind.I4)));
        var callable = resolver.Resolve(functionPointer);

        Assert.Equal((size, size), (reference.Size, reference.Alignment));
        Assert.Equal(0, Assert.Single(reference.ReferenceOffsets));
        Assert.Equal(0, Assert.Single(managedAddress.ReferenceOffsets));
        Assert.Empty(pointer.ReferenceOffsets);
        Assert.Equal((size, size), (callable.Size, callable.Alignment));
        Assert.Empty(callable.ReferenceOffsets);
        Assert.Empty(callable.ByReferenceOffsets);
        Assert.Equal(callable, resolver.Resolve(functionPointer));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void CallingConventionModifiersPreserveUnderlyingStorage(WasmTarget target, bool isRequired)
    {
        var resolver = CreateResolver(target);
        var modifier = CliTypeIdentity.Named(new("System.Runtime"),
            "System.Runtime.CompilerServices", "CallConvCdecl", false);
        foreach (var type in new[]
        {
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            CliTypeIdentity.FromStackKind(CliValueKind.NativeInt),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            CliTypeIdentity.ManagedByReference(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
        })
        {
            var underlying = resolver.Resolve(type);
            var modified = CliTypeIdentity.Modified(type, modifier, isRequired);
            var storage = resolver.Resolve(modified);
            Assert.Equal(modified, storage.Type);
            Assert.Equal((underlying.Size, underlying.Alignment), (storage.Size, storage.Alignment));
            Assert.Equal(underlying.ReferenceOffsets, storage.ReferenceOffsets);
            Assert.Equal(underlying.ByReferenceOffsets, storage.ByReferenceOffsets);
            Assert.Equal(storage, resolver.Resolve(modified));
        }
    }

    [Fact]
    public void RejectsOpenGenericStorage()
    {
        var resolver = CreateResolver(WasmTarget.Wasm32);

        var exception = Assert.Throws<CompilerException>(() =>
            resolver.Resolve(CliTypeIdentity.GenericParameter(method: false, 0)));

        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    [Fact]
    public void CoversPrimitiveCacheAndUnsupportedValueBranches()
    {
        var definitions = new Dictionary<EntityKey, TypeDefinitionModel>();
        var fields = new Dictionary<EntityKey, FieldDefinitionModel>();
        var resolver = new ValueLayoutResolver(
            new Definitions(definitions.Values),
            new Fields(fields.Values),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());

        var primitives = new[]
        {
            ("bool", CliValueKind.I4, 1),
            ("char", CliValueKind.I4, 2),
            ("i1", CliValueKind.I4, 1),
            ("u1", CliValueKind.I4, 1),
            ("i2", CliValueKind.I4, 2),
            ("u2", CliValueKind.I4, 2),
            ("i4", CliValueKind.I4, 4),
            ("u4", CliValueKind.I4, 4),
            ("i8", CliValueKind.I8, 8),
            ("u8", CliValueKind.I8, 8),
            ("f4", CliValueKind.F4, 4),
            ("f8", CliValueKind.F8, 8),
            ("nativeint", CliValueKind.NativeInt, 4),
            ("nativeuint", CliValueKind.NativeInt, 4),
            ("string", CliValueKind.ManagedReference, 4),
            ("object", CliValueKind.ManagedReference, 4),
        };
        foreach (var (name, kind, size) in primitives)
        {
            var type = CliTypeIdentity.Primitive(name, kind);
            Assert.Equal(size, resolver.Resolve(type).Size);
            Assert.Equal(resolver.Resolve(type), resolver.Resolve(type));
        }

        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CliTypeIdentity.Primitive("void", CliValueKind.Void)));
        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CliTypeIdentity.Primitive("unknown", CliValueKind.I4)));
    }

    [Fact]
    public void HandlesDeclaredSizeAndRejectsInvalidValueLayouts()
    {
        var assembly = new AssemblyIdentity("ValueCoverage");
        var recursiveKey = new EntityKey(assembly, 1);
        var recursive = CliTypeIdentity.Named(assembly, "Coverage", "Recursive", true);
        var recursiveFieldKey = new EntityKey(assembly, 2);
        var explicitKey = new EntityKey(assembly, 3);
        var packedKey = new EntityKey(assembly, 4);
        var declaredKey = new EntityKey(assembly, 5);
        var inlineKey = new EntityKey(assembly, 6);
        var definitions = new Definitions([
            new TypeDefinitionModel(
                recursiveKey, "Coverage", "Recursive", true, [recursiveFieldKey], []),
            new TypeDefinitionModel(explicitKey, "Coverage", "Explicit", true, [], [])
            {
                LayoutKind = CliTypeLayoutKind.Explicit,
            },
            new TypeDefinitionModel(packedKey, "Coverage", "Packed", true, [], [])
            {
                PackingSize = 3,
            },
            new TypeDefinitionModel(declaredKey, "Coverage", "Declared", true, [], [])
            {
                DeclaredSize = 2,
            },
            new TypeDefinitionModel(inlineKey, "Coverage", "Inline", true, [], [])
            {
                InlineArrayLength = 2,
            },
        ]);
        var fields = new Fields([
            new FieldDefinitionModel(
                recursiveFieldKey,
                recursiveKey,
                "Value",
                recursive,
                false),
        ]);
        var resolver = new ValueLayoutResolver(
            definitions,
            fields,
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());

        Assert.Throws<CompilerException>(() => resolver.Resolve(recursive));
        Assert.Equal(1, resolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "Explicit", true)).Size);
        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "Packed", true)));
        Assert.Equal(2, resolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "Declared", true)).Size);
        var declaredOne = new TypeDefinitionModel(
            new EntityKey(assembly, 7), "Coverage", "DeclaredOne", true, [], [])
        {
            DeclaredSize = 1,
        };
        var declaredOneResolver = new ValueLayoutResolver(
            new Definitions([declaredOne]),
            new Fields([]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());
        Assert.Equal(1, declaredOneResolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "DeclaredOne", true)).Size);
        var declaredWithFieldKey = new EntityKey(assembly, 8);
        var declaredWithField = new TypeDefinitionModel(
            declaredWithFieldKey,
            "Coverage",
            "DeclaredWithField",
            true,
            [new EntityKey(assembly, 9)],
            [])
        {
            DeclaredSize = 1,
        };
        var declaredWithFieldResolver = new ValueLayoutResolver(
            new Definitions([declaredWithField]),
            new Fields([new FieldDefinitionModel(
                new EntityKey(assembly, 9),
                declaredWithFieldKey,
                "Value",
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                false)]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());
        Assert.Equal(4, declaredWithFieldResolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "DeclaredWithField", true)).Size);

        var fixedBufferKey = new EntityKey(assembly, 10);
        var fixedBuffer = new TypeDefinitionModel(
            fixedBufferKey,
            "Coverage",
            "FixedBuffer",
            true,
            [new EntityKey(assembly, 11)],
            [])
        {
            LayoutKind = CliTypeLayoutKind.Sequential,
            DeclaredSize = 8192,
        };
        var fixedBufferResolver = new ValueLayoutResolver(
            new Definitions([fixedBuffer]),
            new Fields([new FieldDefinitionModel(
                new EntityKey(assembly, 11),
                fixedBufferKey,
                "FixedElementField",
                CliTypeIdentity.Primitive("u4", CliValueKind.I4),
                false)]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm64),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());

        var fixedBufferLayout = fixedBufferResolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "FixedBuffer", true));
        Assert.Equal(8192, fixedBufferLayout.Size);
        Assert.Equal(4, fixedBufferLayout.Alignment);
        Assert.Empty(fixedBufferLayout.ReferenceOffsets);
        Assert.Throws<CompilerException>(() => resolver.Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "Inline", true)));
    }

    [Fact]
    public void ResolvesExplicitDeclaredStorage()
    {
        var assembly = new AssemblyIdentity("ExplicitCoverage");
        var type = CliTypeIdentity.Named(assembly, "Coverage", "Explicit", true);
        var definition = new TypeDefinitionModel(
            new EntityKey(assembly, 1),
            "Coverage",
            "Explicit",
            true,
            [],
            [])
        {
            LayoutKind = CliTypeLayoutKind.Explicit,
            DeclaredSize = 12,
        };
        var resolver = new ValueLayoutResolver(
            new Definitions([definition]),
            new Fields([]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());

        var layout = resolver.Resolve(type);

        Assert.Equal(12, layout.Size);
        Assert.Equal(layout, resolver.Resolve(type));
    }

    [Fact]
    public void ResolveRepopulatesDefinitionFieldsWhenScalarLayoutIsCached()
    {
        var assembly = new AssemblyIdentity("AggregateCoverage");
        var genericKey = new EntityKey(assembly, 1);
        var genericFieldKey = new EntityKey(assembly, 2);
        var genericDefinition = CliTypeIdentity.Named(
            assembly,
            "Coverage",
            "Generic`1",
            true);
        var generic = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var inlineKey = new EntityKey(assembly, 3);
        var inlineFieldKey = new EntityKey(assembly, 4);
        var inline = CliTypeIdentity.Named(assembly, "Coverage", "Inline", true);
        var reference = CliTypeIdentity.Named(
            assembly,
            "Coverage",
            "Reference",
            false);
        var state = new ManagedValueLayoutState();
        var genericFieldKeyRepublished1 = genericFieldKey with { MetadataToken = genericFieldKey.MetadataToken + 100 };
        var inlineFieldKeyRepublished2 = inlineFieldKey with { MetadataToken = inlineFieldKey.MetadataToken + 101 };
        var resolver = new ValueLayoutResolver(
            new Definitions([
                new TypeDefinitionModel(
                    genericKey,
                    "Coverage",
                    "Generic`1",
                    true,
                    [genericFieldKey],
                    []),
                new TypeDefinitionModel(
                    inlineKey,
                    "Coverage",
                    "Inline",
                    true,
                    [inlineFieldKey],
                    [])
                {
                    InlineArrayLength = 2,
                },
            ]),


        new Fields([
            new FieldDefinitionModel(
                    genericFieldKey,
                    genericKey,
                    "Value",
                    CliTypeIdentity.GenericParameter(method: false, 0),
                    false),
                new FieldDefinitionModel(
                    inlineFieldKey,
                    inlineKey,
                    "Element",
                    reference,
                    false),
            new FieldDefinitionModel(genericFieldKeyRepublished1, genericKey, "Value", CliTypeIdentity.GenericParameter(method: false, 0), false),
            new FieldDefinitionModel(inlineFieldKeyRepublished2, inlineKey, "Element", reference, false)]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            state, new FieldLayoutFailureBinder());

        var genericLayout = ((IValueLayoutResolver)resolver).Resolve(generic);

        var publishedDefinitionFields = state.Fields.ToArray();

        var publishedConstructedFields = state.ConstructedFields.ToArray();

        Assert.True(publishedDefinitionFields.Length + publishedConstructedFields.Length > 0);

        state.Fields.Clear();

        state.ConstructedFields.Clear();

        state.CompletedMetadataLayouts.Remove(generic);

        var cachedLayout = ((IValueLayoutResolver)resolver).Resolve(generic);



        Assert.Equal(genericLayout, cachedLayout);

        Assert.Equal(publishedDefinitionFields.Length, state.Fields.Count);

        Assert.Equal(publishedConstructedFields.Length, state.ConstructedFields.Count);

        foreach (var entry in publishedDefinitionFields)

        {

            Assert.True(state.Fields.TryGetValue(entry.Key, out var republished));

            Assert.Equal(entry.Value, republished);

        }

        foreach (var entry in publishedConstructedFields)

        {

            Assert.True(state.ConstructedFields.TryGetValue(entry.Key, out var republished));

            Assert.Equal(entry.Value, republished);

        }
    }

    [Fact]
    public void ResolveRejectsCompletedMetadataWithoutScalarLayout()
    {
        var assembly = new AssemblyIdentity("AggregateCoverage");
        var genericKey = new EntityKey(assembly, 1);
        var genericFieldKey = new EntityKey(assembly, 2);
        var genericDefinition = CliTypeIdentity.Named(
            assembly,
            "Coverage",
            "Generic`1",
            true);
        var generic = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var inlineKey = new EntityKey(assembly, 3);
        var inlineFieldKey = new EntityKey(assembly, 4);
        var inline = CliTypeIdentity.Named(assembly, "Coverage", "Inline", true);
        var reference = CliTypeIdentity.Named(
            assembly,
            "Coverage",
            "Reference",
            false);
        var state = new ManagedValueLayoutState();
        var resolver = new ValueLayoutResolver(
            new Definitions([
                new TypeDefinitionModel(
                    genericKey,
                    "Coverage",
                    "Generic`1",
                    true,
                    [genericFieldKey],
                    []),
                new TypeDefinitionModel(
                    inlineKey,
                    "Coverage",
                    "Inline",
                    true,
                    [inlineFieldKey],
                    [])
                {
                    InlineArrayLength = 2,
                },
            ]),
            new Fields([
                new FieldDefinitionModel(
                    genericFieldKey,
                    genericKey,
                    "Value",
                    CliTypeIdentity.GenericParameter(method: false, 0),
                    false),
                new FieldDefinitionModel(
                    inlineFieldKey,
                    inlineKey,
                    "Element",
                    reference,
                    false),
            ]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            state, new FieldLayoutFailureBinder());

        var genericLayout = ((IValueLayoutResolver)resolver).Resolve(generic);

        state.Values.Remove(generic);


        Assert.Throws<InvalidOperationException>(() => ((IValueLayoutResolver)resolver).Resolve(generic));

        state.Values[generic] = genericLayout;
        var inlineLayout = ((IValueLayoutResolver)resolver).Resolve(inline);

        Assert.Equal(4, genericLayout.Size);
        Assert.Single(state.ConstructedFields);
        Assert.Equal(8, inlineLayout.Size);
        Assert.Collection(
            inlineLayout.ReferenceOffsets,
            offset => Assert.Equal(0, offset),
            offset => Assert.Equal(4, offset));
    }

    [Fact]
    public void PublishesConstructedFieldsAndRepeatsInlineReferences()
    {
        var assembly = new AssemblyIdentity("AggregateCoverage");
        var genericKey = new EntityKey(assembly, 1);
        var genericFieldKey = new EntityKey(assembly, 2);
        var genericDefinition = CliTypeIdentity.Named(
            assembly,
            "Coverage",
            "Generic`1",
            true);
        var generic = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var inlineKey = new EntityKey(assembly, 3);
        var inlineFieldKey = new EntityKey(assembly, 4);
        var inline = CliTypeIdentity.Named(assembly, "Coverage", "Inline", true);
        var reference = CliTypeIdentity.Named(
            assembly,
            "Coverage",
            "Reference",
            false);
        var state = new ManagedValueLayoutState();
        var resolver = new ValueLayoutResolver(
            new Definitions([
                new TypeDefinitionModel(
                    genericKey,
                    "Coverage",
                    "Generic`1",
                    true,
                    [genericFieldKey],
                    []),
                new TypeDefinitionModel(
                    inlineKey,
                    "Coverage",
                    "Inline",
                    true,
                    [inlineFieldKey],
                    [])
                {
                    InlineArrayLength = 2,
                },
            ]),
            new Fields([
                new FieldDefinitionModel(
                    genericFieldKey,
                    genericKey,
                    "Value",
                    CliTypeIdentity.GenericParameter(method: false, 0),
                    false),
                new FieldDefinitionModel(
                    inlineFieldKey,
                    inlineKey,
                    "Element",
                    reference,
                    false),
            ]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            state, new FieldLayoutFailureBinder());

        var genericLayout = resolver.Resolve(generic);
        var inlineLayout = resolver.Resolve(inline);

        Assert.Equal(4, genericLayout.Size);
        Assert.Single(state.ConstructedFields);
        Assert.Equal(8, inlineLayout.Size);
        Assert.Collection(
            inlineLayout.ReferenceOffsets,
            offset => Assert.Equal(0, offset),
            offset => Assert.Equal(4, offset));
    }

    [Fact]
    public void GivesEmptySequentialValuesMinimumStorage()
    {
        var assembly = new AssemblyIdentity("EmptyCoverage");
        var type = CliTypeIdentity.Named(assembly, "Coverage", "Empty", true);
        var resolver = new ValueLayoutResolver(
            new Definitions([new TypeDefinitionModel(
                new EntityKey(assembly, 1),
                "Coverage",
                "Empty",
                true,
                [],
                [])]),
            new Fields([]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());

        Assert.Equal(1, resolver.Resolve(type).Size);
    }

    [Fact]
    public void ResolveOrdersDefinitionFieldsByMetadataToken()
    {
        var assembly = new AssemblyIdentity("LayoutOrdering");
        var typeKey = new EntityKey(assembly, 0x02000001);
        var earlierFieldKey = new EntityKey(assembly, 0x04000001);
        var laterFieldKey = new EntityKey(assembly, 0x04000002);
        var earlierField = new FieldDefinitionModel(
            earlierFieldKey,
            typeKey,
            "Earlier",
            CliValueKind.I4,
            IsStatic: false);
        var laterField = new FieldDefinitionModel(
            laterFieldKey,
            typeKey,
            "Later",
            CliValueKind.I4,
            IsStatic: false);
        var definition = new TypeDefinitionModel(
            typeKey,
            "Coverage",
            "Ordered",
            IsValueType: true,
            [laterFieldKey, earlierFieldKey],
            []);
        var state = new ManagedValueLayoutState();
        var resolver = new ValueLayoutResolver(
            new FixedDefinition(definition),
            new Fields([laterField, earlierField]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            state, new FieldLayoutFailureBinder());

        var layout = ((IValueLayoutResolver)resolver).Resolve(
            CliTypeIdentity.Named(assembly, "Coverage", "Ordered", isValueType: true),
            definition);

        Assert.Equal(8, layout.Size);
        Assert.Equal(0, state.Fields[earlierFieldKey].Offset);
        Assert.Equal(4, state.Fields[laterFieldKey].Offset);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "Boolean", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "Char", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "SByte", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "Byte", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "Int16", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "UInt16", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "Int32", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "UInt32", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm32, "Int64", CliValueKind.I8, 8)]
    [InlineData(WasmTarget.Wasm32, "UInt64", CliValueKind.I8, 8)]
    [InlineData(WasmTarget.Wasm32, "Single", CliValueKind.F4, 4)]
    [InlineData(WasmTarget.Wasm32, "Double", CliValueKind.F8, 8)]
    [InlineData(WasmTarget.Wasm32, "IntPtr", CliValueKind.NativeInt, 4)]
    [InlineData(WasmTarget.Wasm32, "UIntPtr", CliValueKind.NativeInt, 4)]
    [InlineData(WasmTarget.Wasm64, "Boolean", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "Char", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "SByte", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "Byte", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "Int16", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "UInt16", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "Int32", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "UInt32", CliValueKind.I4, 4)]
    [InlineData(WasmTarget.Wasm64, "Int64", CliValueKind.I8, 8)]
    [InlineData(WasmTarget.Wasm64, "UInt64", CliValueKind.I8, 8)]
    [InlineData(WasmTarget.Wasm64, "Single", CliValueKind.F4, 4)]
    [InlineData(WasmTarget.Wasm64, "Double", CliValueKind.F8, 8)]
    [InlineData(WasmTarget.Wasm64, "IntPtr", CliValueKind.NativeInt, 8)]
    [InlineData(WasmTarget.Wasm64, "UIntPtr", CliValueKind.NativeInt, 8)]
    public void ResolvePublishesPrimitiveFacadeBackingField(
        WasmTarget target,
        string typeName,
        CliValueKind valueKind,
        int expectedSize)
    {
        var assembly = new AssemblyIdentity("NetWasm.CoreLib");
        var typeKey = new EntityKey(assembly, 0x02000001);
        var fieldKey = new EntityKey(assembly, 0x04000001);
        var field = new FieldDefinitionModel(
            fieldKey,
            typeKey,
            "_value",
            valueKind,
            IsStatic: false);
        var definition = new TypeDefinitionModel(
            typeKey,
            "System",
            typeName,
            IsValueType: true,
            [fieldKey],
            []);
        var state = new ManagedValueLayoutState();
        var definitions = new FixedDefinition(definition);
        Assert.Single(definition.Fields);
        var resolver = new ValueLayoutResolver(
            definitions,
            new Fields([field]),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(target),
            state, new FieldLayoutFailureBinder());

        var primitive = CliTypeIdentity.FromStackKind(valueKind);
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(primitive, null!));
        Assert.Empty(state.Values);
        Assert.Empty(state.Fields);

        var scalarLayout = resolver.Resolve(primitive);
        Assert.Equal(expectedSize, scalarLayout.Size);
        Assert.DoesNotContain(primitive, state.CompletedMetadataLayouts);
        Assert.Empty(state.Fields);

        var layout = resolver.Resolve(primitive, definition);

        Assert.Equal(expectedSize, layout.Size);
        Assert.Single(state.Fields);
        Assert.True(state.Fields.TryGetValue(fieldKey, out var fieldLayout));
        Assert.Equal(0, fieldLayout.Offset);
        Assert.Equal(expectedSize, fieldLayout.Size);
        Assert.Contains(primitive, state.CompletedMetadataLayouts);
    }

    [Fact]
    public void ConstructorRejectsEveryMissingDependency()
    {
        var assembly = new AssemblyIdentity("EmptyCoverage");
        var type = CliTypeIdentity.Named(assembly, "Coverage", "Empty", true);
        Assert.Throws<ArgumentNullException>(() =>
            new ValueLayoutResolver(null!, new Fields([]), new ExplicitValueLayoutResolver(), WasmTargetLayout.For(WasmTarget.Wasm32), new ManagedValueLayoutState(), new FieldLayoutFailureBinder()));
        Assert.Throws<ArgumentNullException>(() =>
            new ValueLayoutResolver(new Definitions([new TypeDefinitionModel(
                new EntityKey(assembly, 1),
                "Coverage",
                "Empty",
                true,
                [],
                [])]), null!, new ExplicitValueLayoutResolver(), WasmTargetLayout.For(WasmTarget.Wasm32), new ManagedValueLayoutState(), new FieldLayoutFailureBinder()));
        Assert.Throws<ArgumentNullException>(() =>
            new ValueLayoutResolver(new Definitions([new TypeDefinitionModel(
                new EntityKey(assembly, 1),
                "Coverage",
                "Empty",
                true,
                [],
                [])]), new Fields([]), null!, WasmTargetLayout.For(WasmTarget.Wasm32), new ManagedValueLayoutState(), new FieldLayoutFailureBinder()));
        Assert.Throws<ArgumentNullException>(() =>
            new ValueLayoutResolver(new Definitions([new TypeDefinitionModel(
                new EntityKey(assembly, 1),
                "Coverage",
                "Empty",
                true,
                [],
                [])]), new Fields([]), new ExplicitValueLayoutResolver(), null!, new ManagedValueLayoutState(), new FieldLayoutFailureBinder()));
        Assert.Throws<ArgumentNullException>(() =>
            new ValueLayoutResolver(new Definitions([new TypeDefinitionModel(
                new EntityKey(assembly, 1),
                "Coverage",
                "Empty",
                true,
                [],
                [])]), new Fields([]), new ExplicitValueLayoutResolver(), WasmTargetLayout.For(WasmTarget.Wasm32), null!, new FieldLayoutFailureBinder()));
    }

    private static ValueLayoutResolver CreateResolver(WasmTarget target)
        => new ValueLayoutResolver(
            new RejectingTypeDefinitions(),
            new RejectingFields(),
            new ExplicitValueLayoutResolver(),
            WasmTargetLayout.For(target),
            new ManagedValueLayoutState(), new FieldLayoutFailureBinder());

    private sealed class FixedDefinition(TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => definition;
    }

    private sealed class RejectingTypeDefinitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            throw new NotSupportedException(identity.CanonicalName);
    }

    private sealed class RejectingFields : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new NotSupportedException(key.ToString());
    }

    private sealed class Definitions(IEnumerable<TypeDefinitionModel> definitions) :
        ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName,
                StringComparer.Ordinal);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _definitions[identity.FullName ?? identity.ElementType!.FullName!];
    }

    private sealed class Fields(IEnumerable<FieldDefinitionModel> fields) : IFieldRepository
    {
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields =
            fields.ToDictionary(field => field.Key);

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];
    }
}
