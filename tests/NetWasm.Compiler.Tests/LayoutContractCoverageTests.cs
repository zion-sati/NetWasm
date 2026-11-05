using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class LayoutContractCoverageTests
{
    private static readonly AssemblyIdentity Assembly = new("LayoutCoverage");
    private static readonly EntityKey TypeKey = new(Assembly, 1);
    private static readonly EntityKey FieldKey = new(Assembly, 2);
    private static readonly CliTypeIdentity Type =
        CliTypeIdentity.Named(Assembly, "Coverage", "Node", isValueType: false);

    [Fact]
    public void ParallelLayoutCompositionRejectsMissingAndIncompatibleCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AssignableTypeMetadataBuilderFactory(null!,
                new TypeRelationshipClassifierFactory(), new TypeRelationshipKeyResolverFactory()));
        Assert.Throws<ArgumentNullException>(() =>
            new AssignableTypeMetadataBuilderFactory(
                new UnusedImplementedInterfaceResolverFactory(), null!, new TypeRelationshipKeyResolverFactory()));
        Assert.Throws<ArgumentNullException>(() =>
            new AssignableTypeMetadataBuilderFactory(
                new UnusedImplementedInterfaceResolverFactory(), new TypeRelationshipClassifierFactory(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedLayoutForkPublisher(null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedLayoutForkPublisher(new ManagedValueLayoutState(), null!));

        var types = new TypeDefinitionsByKey([]);
        var factory = new ManagedLayoutForkSourceFactory(null!, null!, null!);
        Assert.Throws<ArgumentException>(() => factory.Create(
            new UnmanagedTargetLayout(), types,
            new TypeDefinitions([]), new UnusedFieldRepository()));
    }

    [Theory]
    [InlineData(ManagedExceptionKind.NullReference, "System.NullReferenceException")]
    [InlineData(ManagedExceptionKind.IndexOutOfRange, "System.IndexOutOfRangeException")]
    [InlineData(ManagedExceptionKind.DivideByZero, "System.DivideByZeroException")]
    [InlineData(ManagedExceptionKind.Arithmetic, "System.ArithmeticException")]
    [InlineData(ManagedExceptionKind.Overflow, "System.OverflowException")]
    [InlineData(ManagedExceptionKind.InvalidCast, "System.InvalidCastException")]
    [InlineData(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException")]
    [InlineData(ManagedExceptionKind.Argument, "System.ArgumentException")]
    [InlineData(ManagedExceptionKind.ArgumentNull,
        "System.ArgumentNullException")]
    [InlineData(ManagedExceptionKind.ArgumentOutOfRange,
        "System.ArgumentOutOfRangeException")]
    [InlineData(ManagedExceptionKind.ArrayTypeMismatch,
        "System.ArrayTypeMismatchException")]
    [InlineData(ManagedExceptionKind.JSException, "System.JSException")]
    [InlineData(ManagedExceptionKind.InvalidOperation,
        "System.InvalidOperationException")]
    [InlineData(ManagedExceptionKind.NotSupported, "System.NotSupportedException")]
    public void ResolvesEveryImplicitExceptionName(
        ManagedExceptionKind kind,
        string expected)
    {
        Assert.Equal(expected, new ExceptionTypeNameResolver().Resolve(kind));
    }

    [Fact]
    public void RejectsUnknownImplicitExceptionKinds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExceptionTypeNameResolver().Resolve((ManagedExceptionKind)int.MaxValue));
    }

    [Fact]
    public void SnapshotExposesEveryLayoutContract()
    {
        var objectLayout = new ObjectLayout(7, 12, [4]);
        var valueLayout = new ValueLayout(Type, 8, 4, [0]);
        var fieldLayout = new FieldLayout(4) { Size = 8, Type = Type };
        var staticFieldLayout = new StaticFieldLayout(32);
        var stringLayout = new StringLayout(48, 5, 8);
        var constructed = CliTypeIdentity.GenericInstantiation(Type, [Type]);
        var constructedField = new FieldLayout(16) { Size = 4, Type = Type };
        var constructedStatic = new StaticFieldLayout(64);
        var dataSegment = new DataSegment(48, [1, 2, 3]);
        var state = new ManagedValueLayoutState();
        state.Fields.Add(FieldKey, fieldLayout);
        state.ConstructedFields.Add("constructed", constructedField);
        state.Values.Add(Type, valueLayout);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(TypeKey, objectLayout),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(Type, objectLayout),
            ImmutableDictionary<string, ObjectLayout>.Empty
                .Add("System.Array", new ObjectLayout(1, 8, []))
                .Add("System.String", new ObjectLayout(2, 8, []))
                .Add("System.Type", new ObjectLayout(3, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(constructed, objectLayout),
            state,
            WasmTargetLayout.For(WasmTarget.Wasm64),
            1,
            2,
            3,
            4);
        var staticData = new ManagedStaticData(
            80,
            ImmutableDictionary<EntityKey, StaticFieldLayout>.Empty
                .Add(FieldKey, staticFieldLayout),
            ImmutableDictionary<string, StaticFieldLayout>.Empty
                .Add("constructed", constructedStatic),
            ImmutableDictionary<string, StringLayout>.Empty.Add("hello", stringLayout),
            ImmutableDictionary<ManagedExceptionKind, int>.Empty.Add(
                ManagedExceptionKind.NullReference,
                56),
            [dataSegment],
            [new(TypeKey, 7, 0, 12, 80, 2, null)],
            [new(constructed, 7, 0, 12, 88, 2, null)],
            [new(Type, 7, 8, 8, 96, 2)],
            [32]);
        var snapshot = new ManagedLayoutSnapshot(types, staticData);

        Assert.Equal(WasmTarget.Wasm64, snapshot.Target.Target);
        Assert.Equal(2, snapshot.StringTypeId);
        Assert.Equal(3, snapshot.TypeTypeId);
        Assert.Equal(80, snapshot.StaticDataEnd);
        Assert.Equal(8, snapshot.StringLengthOffset);
        Assert.Equal(12, snapshot.StringDataOffset);
        Assert.Equal(8, snapshot.ArrayLengthOffset);
        Assert.Equal(16, snapshot.ArrayDataPointerOffset);
        Assert.Equal(24, snapshot.ArrayElementTypeIdOffset);
        // Elements of a one-dimensional array sit inline, where the runtime's
        // NETWASM_ARRAY_INLINE_DATA_OFFSET puts them.
        Assert.Equal(32, snapshot.Target.ArrayInlineDataOffset);
        Assert.Equal(16, WasmTargetLayout.Wasm32.ArrayInlineDataOffset);
        Assert.Equal(1, snapshot.DelegateTargetOffset);
        Assert.Equal(2, snapshot.DelegateMethodIdOffset);
        Assert.Equal(3, snapshot.DelegateInvocationListOffset);
        Assert.Equal(4, snapshot.DelegateInvocationCountOffset);
        Assert.Equal(staticData.Segments, snapshot.DataSegments);
        Assert.Equal(staticData.TypeDescriptors, snapshot.TypeDescriptors);
        Assert.Equal(staticData.ConstructedTypeDescriptors,
            snapshot.ConstructedTypeDescriptors);
        Assert.Equal(staticData.ValueTypeDescriptors, snapshot.ValueTypeDescriptors);
        Assert.Equal(staticData.StaticRootAddresses, snapshot.StaticRootAddresses);
        Assert.Same(types.Objects, snapshot.ObjectLayouts);
        Assert.Same(types.ValueLayoutState.Values, snapshot.ValueLayouts);
    }

    [Fact]
    public void AssignableTypeMetadataIgnoresOpenGenericParameterLayouts()
    {
        var interfaceKey = new EntityKey(Assembly, 20);
        var interfaceType = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "IItem",
            isValueType: false);
        var interfaceDefinition = new TypeDefinitionModel(
            interfaceKey,
            "Coverage",
            "IItem",
            false,
            [],
            [])
        {
            IsInterface = true,
        };
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(
                interfaceKey,
                new ObjectLayout(7, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(
                CliTypeIdentity.GenericParameter(method: false, index: 0),
                new ObjectLayout(8, 8, [])),
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var state = new ManagedStaticDataBuildState();
        var builder = new AssignableTypeMetadataBuilder(
            new FixedIdentityResolver(interfaceType),
            new TypeDefinitions([
                interfaceDefinition,
                Definition(TypeKey, "Coverage.Node", valueType: false),
            ]),
            new NoTypeRelationships(),
            UnboundedTypeRelationshipKeys.Instance,
            types,
            state);

        var result = builder.Build(Type);

        Assert.Equal(default, result);
        Assert.Empty(state.Segments);
    }

    [Fact]
    public void AssignableTypeMetadataIgnoresOpenGenericDefinitions()
    {
        var interfaceKey = new EntityKey(Assembly, 20);
        var interfaceType = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "IItem",
            isValueType: false);
        var interfaceDefinition = new TypeDefinitionModel(
            interfaceKey,
            "Coverage",
            "IItem",
            false,
            [],
            [])
        {
            IsInterface = true,
        };
        var genericType = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "Container`1",
            isValueType: false);
        var genericDefinition = new TypeDefinitionModel(
            new EntityKey(Assembly, 21),
            "Coverage",
            "Container`1",
            false,
            [],
            [])
        {
            GenericArity = 1,
        };
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(
                interfaceKey,
                new ObjectLayout(7, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var state = new ManagedStaticDataBuildState();
        var builder = new AssignableTypeMetadataBuilder(
            new FixedIdentityResolver(interfaceType),
            new TypeDefinitions([interfaceDefinition, genericDefinition]),
            new ThrowingTypeRelationships(),
            UnboundedTypeRelationshipKeys.Instance,
            types,
            state);

        var result = builder.Build(genericType);

        Assert.Equal(default, result);
        Assert.Empty(state.Segments);
    }

    [Theory]
    [InlineData("unbounded")]
    [InlineData("filed")]
    [InlineData("unfiled-array")]
    public void AssignableTypeMetadataWritesClosedArrayAndVariantTargets(string keys)
    {
        var argument = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "Argument",
            isValueType: false);
        var array = CliTypeIdentity.SzArray(argument);
        var unsupported = CliTypeIdentity.ManagedByReference(argument);
        var classTarget = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "ClassTarget",
            isValueType: false);
        var variantDefinitionType = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "Producer`1",
            isValueType: false);
        var invariantDefinitionType = CliTypeIdentity.Named(
            Assembly,
            "Coverage",
            "Container`1",
            isValueType: false);
        var variant = CliTypeIdentity.GenericInstantiation(
            variantDefinitionType,
            [argument]);
        var invariant = CliTypeIdentity.GenericInstantiation(
            invariantDefinitionType,
            [argument]);
        var openVariant = CliTypeIdentity.GenericInstantiation(
            variantDefinitionType,
            [CliTypeIdentity.GenericParameter(method: false, index: 0)]);
        var variantDefinition = Definition(
            new EntityKey(Assembly, 22),
            "Coverage.Producer`1",
            valueType: false) with
        {
            GenericArity = 1,
            GenericParameterVariances = [CliGenericVariance.Covariant],
        };
        var invariantDefinition = Definition(
            new EntityKey(Assembly, 23),
            "Coverage.Container`1",
            valueType: false) with
        {
            GenericArity = 1,
            GenericParameterVariances = [CliGenericVariance.Invariant],
        };
        var argumentDefinition = Definition(
            new EntityKey(Assembly, 24),
            "Coverage.Argument",
            valueType: false);
        var classDefinition = Definition(
            new EntityKey(Assembly, 25),
            "Coverage.ClassTarget",
            valueType: false);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(array, new ObjectLayout(9, 8, []))
                .Add(variant, new ObjectLayout(7, 8, []))
                .Add(invariant, new ObjectLayout(8, 8, []))
                .Add(openVariant, new ObjectLayout(10, 8, []))
                .Add(unsupported, new ObjectLayout(11, 8, []))
                .Add(classTarget, new ObjectLayout(12, 8, [])),
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var state = new ManagedStaticDataBuildState { Cursor = 2 };
        var builder = new AssignableTypeMetadataBuilder(
            new FixedIdentityResolver(argument),
            new TypeDefinitions([
                variantDefinition,
                invariantDefinition,
                argumentDefinition,
                classDefinition,
            ]),
            new AllTypeRelationships(),
            keys switch
            {
                "unbounded" => UnboundedTypeRelationshipKeys.Instance,
                // The candidate reaches the variant's key and the array's, or not the array's,
                // which an unbounded array is classified against anyway.
                "filed" => new KeyTable(argument, ["variant", "array"], array, unboundedArray: false),
                _ => new KeyTable(argument, ["variant"], array, unboundedArray: true),
            },
            types,
            state);

        var result = builder.Build(argument);

        Assert.Equal(new AssignableTypeMetadataLayout(4, 2), result);
        Assert.Equal(12, state.Cursor);
        var segment = Assert.Single(state.Segments);
        Assert.Equal(4, segment.Address);
        Assert.Equal(
            (byte[])[7, 0, 0, 0, 9, 0, 0, 0],
            segment.Data);
    }

    [Fact]
    public void AssignableTypeMetadataClassifiesOnlyTargetsUnderReachedKeys()
    {
        var argument = CliTypeIdentity.Named(Assembly, "Coverage", "Argument", isValueType: false);
        var array = CliTypeIdentity.SzArray(argument);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty,
            ImmutableDictionary<string, ObjectLayout>.Empty,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(array, new ObjectLayout(9, 8, [])),
            new ManagedValueLayoutState(),
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        var builder = new AssignableTypeMetadataBuilder(
            new FixedIdentityResolver(argument),
            new TypeDefinitions([Definition(new EntityKey(Assembly, 24), "Coverage.Argument", valueType: false)]),
            new AllTypeRelationships(),
            new KeyTable(argument, ["variant"], array, unboundedArray: false),
            types,
            new ManagedStaticDataBuildState());

        Assert.Equal(default, builder.Build(argument));
    }

    // The candidate reaches the named keys; the array target files under "array" unless unbounded.
    private sealed class KeyTable(
        CliTypeIdentity candidate,
        string[] reached,
        CliTypeIdentity array,
        bool unboundedArray) : ITypeRelationshipKeyResolver
    {
        public TypeRelationshipKeys Resolve(CliTypeIdentity type) =>
            type == candidate ? new("candidate", reached.ToHashSet(StringComparer.Ordinal), false) :
            type == array ? new("array", new HashSet<string> { "array" }, unboundedArray) :
            type.Shape == CliTypeShape.GenericInstantiation ? new("variant", new HashSet<string> { "variant" }, false) :
            new(type.CanonicalName, new HashSet<string> { type.CanonicalName }, false);
    }

    [Fact]
    public void ProvidersReadAndRejectDefinitionAndInstanceLayouts()
    {
        var snapshot = CreateSnapshot();
        var field = new FieldDefinitionModel(FieldKey, TypeKey, "Value", Type, false);
        var instance = new FieldInstanceModel(field, Type, Type);
        var constructed = instance with
        {
            DeclaringType = CliTypeIdentity.GenericInstantiation(
            Type,
            [Type])
        };
        var missing = new EntityKey(Assembly, 999);

        var instances = new InstanceFieldLayoutProvider(snapshot);
        var statics = new StaticFieldLayoutProvider(snapshot);
        Assert.Equal(4, instances.GetFieldLayout(FieldKey).Offset);
        Assert.Equal(4, instances.GetFieldLayout(instance).Offset);
        Assert.Equal(16, instances.GetFieldLayout(constructed).Offset);
        Assert.Throws<CompilerException>(() => instances.GetFieldLayout(missing));
        Assert.Throws<CompilerException>(() => instances.GetFieldLayout(
            constructed with
            {
                DeclaringType = CliTypeIdentity.GenericInstantiation(
                Type, [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
            }));

        Assert.Equal(32, statics.GetStaticFieldLayout(FieldKey).Address);
        Assert.Equal(32, statics.GetStaticFieldLayout(instance).Address);
        Assert.Equal(64, statics.GetStaticFieldLayout(constructed).Address);
        Assert.Throws<CompilerException>(() => statics.GetStaticFieldLayout(missing));
        Assert.Throws<CompilerException>(() => statics.GetStaticFieldLayout(
            constructed with
            {
                DeclaringType = CliTypeIdentity.GenericInstantiation(
                Type, [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])
            }));
    }

    [Fact]
    public void ProvidersReadAndRejectStaticDataAndExceptionLayouts()
    {
        var snapshot = CreateSnapshot();
        var data = new StaticDataLayoutProvider(snapshot);
        var exceptions = new ManagedExceptionObjectProvider(snapshot);

        Assert.Equal(80, data.StaticDataEnd);
        Assert.Equal(32, Assert.Single(data.StaticRootAddresses));
        Assert.Equal(48, Assert.Single(data.DataSegments).Address);
        Assert.Equal(48, data.GetStringLayout("hello").Address);
        Assert.Throws<CompilerException>(() => data.GetStringLayout("missing"));
        Assert.Equal(56, exceptions.GetExceptionObject(ManagedExceptionKind.NullReference));
        Assert.Throws<CompilerException>(() => exceptions.GetExceptionObject(
            ManagedExceptionKind.DivideByZero));
    }

    [Fact]
    public void TypeLayoutProviderResolvesEntityNamedConstructedAndPrimitiveLayouts()
    {
        var (snapshot, primitiveDefinitions) = CreatePrimitiveSnapshot();
        var definitions = new TypeDefinitions(
        [
            .. primitiveDefinitions,
            Definition(new EntityKey(Assembly, 1000), "Coverage.Missing", false),
        ]);
        var provider = new TypeLayoutProvider(definitions, snapshot);

        Assert.Equal(2, provider.StringTypeId);
        Assert.Equal(3, provider.TypeTypeId);
        var entityLayout = provider.GetObjectLayout(TypeKey);
        Assert.Equal(7, entityLayout.TypeId);
        Assert.Equal(12, entityLayout.Size);
        Assert.True(entityLayout.ReferenceOffsets.AsSpan().SequenceEqual([4]));
        var namedLayout = provider.GetObjectLayout(Type);
        Assert.True(provider.GetObjectLayout(Type, out var optionalNamedLayout));
        Assert.Equal(namedLayout, optionalNamedLayout);
        Assert.Equal(7, namedLayout.TypeId);
        Assert.Equal(12, namedLayout.Size);
        Assert.True(namedLayout.ReferenceOffsets.AsSpan().SequenceEqual([4]));
        Assert.Equal(new ObjectLayout(9, 16, []), provider.GetObjectLayout(
            CliTypeIdentity.GenericInstantiation(Type, [Type])));
        Assert.Equal(new ObjectLayout(8, 8, []), provider.GetObjectLayout(
            CliTypeIdentity.Named(Assembly, "Coverage", "Missing", false)));

        var primitiveNames = new[]
        {
            "void", "bool", "char", "i1", "u1", "i2", "u2", "i4", "u4",
            "i8", "u8", "f4", "f8", "nativeint", "nativeuint", "string", "object",
        };
        foreach (var primitive in primitiveNames)
        {
            var layout = provider.GetObjectLayout(
                CliTypeIdentity.Primitive(primitive, CliValueKind.I4));
            Assert.Equal(primitive == "string" ? 2 : 8, layout.TypeId);
            Assert.Equal(8, layout.Size);
            Assert.Empty(layout.ReferenceOffsets);
        }
        var objectKey = primitiveDefinitions.Single(definition =>
            definition.FullName == "System.Object").Key;
        var withoutObject = new ManagedLayoutSnapshot(
            snapshot.TypeLayouts with
            {
                Objects = snapshot.TypeLayouts.Objects.Remove(objectKey),
            },
            snapshot.StaticData);
        Assert.False(new TypeLayoutProvider(definitions, withoutObject).GetObjectLayout(
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference),
            out _));
        Assert.Throws<CompilerException>(() => new TypeLayoutProvider(
            definitions, withoutObject).GetObjectLayout(
                CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference)));

        Assert.Throws<CompilerException>(() => provider.GetObjectLayout(
            new EntityKey(Assembly, 999)));
        Assert.Throws<CompilerException>(() => provider.GetObjectLayout(
            CliTypeIdentity.Primitive("unsupported", CliValueKind.I4)));
        Assert.Throws<CompilerException>(() => provider.GetObjectLayout(
            CliTypeIdentity.SzArray(Type)));
    }

    [Fact]
    public void ValueLayoutProviderDelegatesThroughItsInjectedResolver()
    {
        var expected = new ValueLayout(Type, 4, 4, []);
        var provider = new ValueLayoutProvider(new FixedValueResolver(expected));

        Assert.Equal(expected, provider.GetValueLayout(Type));
    }
    [Fact]
    public void BitmapBuilderRejectsUnsupportedWordSizes()
    {
        var builder = new StaticReferenceBitmapBuilder();
        Assert.Empty(builder.Build([], 0, sizeof(uint)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build([0], 1, 2));
    }

    private static ManagedLayoutSnapshot CreateSnapshot()
    {
        var objectLayout = new ObjectLayout(7, 12, [4]);
        var valueLayout = new ValueLayout(Type, 8, 4, [0]);
        var state = new ManagedValueLayoutState();
        state.Fields.Add(FieldKey, new FieldLayout(4) { Size = 8, Type = Type });
        var field = new FieldDefinitionModel(FieldKey, TypeKey, "Value", Type, false);
        var constructed = CliTypeIdentity.GenericInstantiation(Type, [Type]);
        var constructedField = new FieldInstanceModel(field, constructed, Type);
        state.ConstructedFields.Add(constructedField.CanonicalName, new FieldLayout(16)
        {
            Size = 4,
            Type = Type,
        });
        state.Values.Add(Type, valueLayout);
        var types = new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty.Add(TypeKey, objectLayout),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(Type, objectLayout),
            ImmutableDictionary<string, ObjectLayout>.Empty
                .Add("System.Array", new ObjectLayout(1, 8, []))
                .Add("System.String", new ObjectLayout(2, 8, []))
                .Add("System.Type", new ObjectLayout(3, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(constructed, objectLayout),
            state,
            WasmTargetLayout.For(WasmTarget.Wasm32),
            1,
            2,
            3,
            4);
        var staticData = new ManagedStaticData(
            80,
            ImmutableDictionary<EntityKey, StaticFieldLayout>.Empty.Add(
                FieldKey,
                new StaticFieldLayout(32)),
            ImmutableDictionary<string, StaticFieldLayout>.Empty.Add(
                constructedField.CanonicalName,
                new StaticFieldLayout(64)),
            ImmutableDictionary<string, StringLayout>.Empty.Add(
                "hello",
                new StringLayout(48, 5, 8)),
            ImmutableDictionary<ManagedExceptionKind, int>.Empty.Add(
                ManagedExceptionKind.NullReference,
                56),
            [new DataSegment(48, [1, 2, 3])],
            [],
            [],
            [],
            [32]);
        return new ManagedLayoutSnapshot(types, staticData);
    }

    private static (ManagedLayoutSnapshot Snapshot, TypeDefinitionModel[] Definitions)
        CreatePrimitiveSnapshot()
    {
        var names = new[]
        {
            "System.Void", "System.Boolean", "System.Char", "System.SByte",
            "System.Byte", "System.Int16", "System.UInt16", "System.Int32",
            "System.UInt32", "System.Int64", "System.UInt64", "System.Single",
            "System.Double", "System.IntPtr", "System.UIntPtr", "System.String",
            "System.Object", "System.Array", "System.Type",
        };
        var definitions = names.Select((name, index) =>
            Definition(new EntityKey(Assembly, 100 + index), name, false)).ToArray();
        var objects = definitions.ToDictionary(
            definition => definition.Key,
            definition => definition.FullName switch
            {
                "System.Array" => new ObjectLayout(1, 8, []),
                "System.String" => new ObjectLayout(2, 8, []),
                "System.Type" => new ObjectLayout(3, 8, []),
                _ => new ObjectLayout(8, 8, []),
            }).ToImmutableDictionary();
        var byName = objects
            .Where(pair => definitions.Single(definition => definition.Key == pair.Key)
                .FullName is "System.Array" or "System.String" or "System.Type")
            .ToImmutableDictionary(
                pair => definitions.Single(definition => definition.Key == pair.Key).FullName,
                pair => pair.Value,
                StringComparer.Ordinal);
        var state = new ManagedValueLayoutState();
        var types = new ManagedTypeLayouts(
            objects
                .Add(TypeKey, new ObjectLayout(7, 12, [4]))
                .Add(new EntityKey(Assembly, 1000), new ObjectLayout(8, 8, [])),
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(Type, new ObjectLayout(7, 12, [4])),
            byName,
            ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
                .Add(CliTypeIdentity.GenericInstantiation(Type, [Type]),
                    new ObjectLayout(9, 16, [])),
            state,
            WasmTargetLayout.For(WasmTarget.Wasm32),
            -1,
            -1,
            -1,
            -1);
        return (new ManagedLayoutSnapshot(
            types,
            new ManagedStaticData(16, [], [], [], [], [], [], [], [], [])), definitions);
    }

    private static TypeDefinitionModel Definition(
        EntityKey key,
        string fullName,
        bool valueType)
    {
        var separator = fullName.LastIndexOf('.');
        return new TypeDefinitionModel(
            key,
            separator < 0 ? "" : fullName[..separator],
            separator < 0 ? fullName : fullName[(separator + 1)..],
            valueType,
            [],
            []);
    }

    private sealed class FixedValueResolver(ValueLayout layout) : IValueLayoutResolver
    {
        public ValueLayout Resolve(CliTypeIdentity type) => layout;

        public ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition)
            => Resolve(type);
    }

    private sealed class FixedIdentityResolver(CliTypeIdentity identity) : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => identity;
    }

    private sealed class NoTypeRelationships : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) => default;
    }

    private sealed class AllTypeRelationships : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) => new(
                TypeRelationshipCharacteristics.Hierarchy);
    }

    private sealed class ThrowingTypeRelationships : ITypeRelationshipClassifier
    {
        public TypeRelationship Classify(
            CliTypeIdentity candidate,
            CliTypeIdentity target) =>
            throw new InvalidOperationException("open generic types are not runtime candidates");
    }

    private sealed class TypeDefinitions(IEnumerable<TypeDefinitionModel> definitions) :
        ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName,
                StringComparer.Ordinal);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _definitions[identity.Shape == CliTypeShape.Primitive
                ? identity.CanonicalName switch
                {
                    "primitive:void" => "System.Void",
                    "primitive:bool" => "System.Boolean",
                    "primitive:char" => "System.Char",
                    "primitive:i1" => "System.SByte",
                    "primitive:u1" => "System.Byte",
                    "primitive:i2" => "System.Int16",
                    "primitive:u2" => "System.UInt16",
                    "primitive:i4" => "System.Int32",
                    "primitive:u4" => "System.UInt32",
                    "primitive:i8" => "System.Int64",
                    "primitive:u8" => "System.UInt64",
                    "primitive:f4" => "System.Single",
                    "primitive:f8" => "System.Double",
                    "primitive:nativeint" => "System.IntPtr",
                    "primitive:nativeuint" => "System.UIntPtr",
                    "primitive:string" => "System.String",
                    "primitive:object" => "System.Object",
                    _ => throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.UnsupportedMetadata,
                        "unsupported primitive")),
                }
                : (identity.Shape == CliTypeShape.GenericInstantiation
                    ? identity.ElementType!.FullName
                    : identity.FullName)!];
    }

    private sealed class TypeDefinitionsByKey(IEnumerable<TypeDefinitionModel> definitions) :
        ITypeRepository
    {
        private readonly Dictionary<EntityKey, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.Key);

        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => _definitions[key];
    }

    private sealed class UnusedImplementedInterfaceResolverFactory :
        IImplementedInterfaceResolverFactory
    {
        public IImplementedInterfaceResolver Create(
            MetadataCompilationSnapshot snapshot) => throw new NotSupportedException();
    }

    private sealed class UnusedFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new NotSupportedException();
    }

    private sealed class UnmanagedTargetLayout : ITargetLayout
    {
        public WasmTargetLayout Target => WasmTargetLayout.For(WasmTarget.Wasm32);
    }
}
