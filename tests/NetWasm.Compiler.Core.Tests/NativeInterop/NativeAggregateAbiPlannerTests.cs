using System.Collections.Immutable;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

public sealed class NativeAggregateAbiPlannerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ClassifiesProducerQualifiedSingletonsIncludingNestedUnionAndInlineArray(WasmTarget target)
    {
        var fixture = new Fixture(target);
        foreach (var scalar in new[]
        {
            CliTypeIdentity.Primitive("i1", CliValueKind.I4),
            CliTypeIdentity.Primitive("u2", CliValueKind.I4),
            CliTypeIdentity.Primitive("u4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i8", CliValueKind.I8),
            CliTypeIdentity.Primitive("u8", CliValueKind.I8),
            CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
            CliTypeIdentity.Primitive("nativeuint", CliValueKind.NativeInt),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            CliTypeIdentity.Primitive("i2", CliValueKind.I4),
            CliTypeIdentity.Primitive("u1", CliValueKind.I4),
            CliTypeIdentity.Primitive("f4", CliValueKind.F4),
            CliTypeIdentity.Primitive("f8", CliValueKind.F8),
            CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.FromStackKind(CliValueKind.Void)),
        })
        {
            var storage = fixture.GetValueLayout(scalar);
            var aggregate = fixture.Add(storage.Size, storage.Alignment, [(scalar, 0)]);
            var singleton = fixture.Add(storage.Size, storage.Alignment, [(aggregate, 0)]);
            var union = fixture.Add(storage.Size, storage.Alignment, [(scalar, 0)], CliTypeLayoutKind.Explicit);
            var inlineArray = fixture.Add(storage.Size, storage.Alignment, [(scalar, 0)], inlineArrayLength: 1);
            foreach (var type in new[] { aggregate, singleton, union, inlineArray })
            {
                var plan = fixture.Planner().Plan(type, "native-call");

                Assert.Equal(NativeAbiValueKind.ScalarizedAggregate, plan.Kind);
                Assert.Equal(type, plan.LogicalType);
                Assert.Equal(scalar.Shape == CliTypeShape.UnmanagedPointer
                    ? CliTypeIdentity.FromStackKind(CliValueKind.NativeInt) : scalar, plan.PhysicalType);
                Assert.Equal(plan.PhysicalType, plan.ScalarStorageType);
                Assert.Equal(storage.Size, plan.Size);
                Assert.Equal(storage.Alignment, plan.Alignment);
                Assert.Equal(0, plan.ScalarOffset);
            }
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OtherAggregatesRequireCallerOwnedIndirectStorage(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var real = CliTypeIdentity.FromStackKind(CliValueKind.F8);
        var single = CliTypeIdentity.FromStackKind(CliValueKind.F4);
        var pointer = CliTypeIdentity.UnmanagedPointer(integer);
        var pair = fixture.Add(16, 8, [(integer, 0), (real, 8)]);
        var nested = fixture.Add(24, 8, [(pair, 0), (pointer, 16)]);
        var union = fixture.Add(4, 4, [(integer, 0), (single, 0)], CliTypeLayoutKind.Explicit);
        var array = fixture.Add(8, 4, [(integer, 0)], inlineArrayLength: 2);
        var padded = fixture.Add(16, 4, [(integer, 0)], declaredSize: 16);
        var aligned = fixture.Add(16, 16, [(integer, 0)]);
        var shifted = fixture.Add(8, 4, [(integer, 4)], CliTypeLayoutKind.Explicit);
        var opaque = fixture.Add(16, 1, [], declaredSize: 16);
        var embeddedOpaque = fixture.Add(20, 4, [(opaque, 0), (integer, 16)]);
        foreach (var type in new[] { pair, nested, union, array, padded, aligned, shifted, opaque, embeddedOpaque })
        {
            var plan = fixture.Planner().Plan(type, "native-call");

            Assert.Equal(NativeAbiValueKind.IndirectAggregate, plan.Kind);
            Assert.Equal(type, plan.LogicalType);
            Assert.Equal(CliValueKind.NativeInt, plan.PhysicalType!.StackKind);
            Assert.Equal(fixture.GetValueLayout(type).Size, plan.Size);
            Assert.Equal(fixture.GetValueLayout(type).Alignment, plan.Alignment);
            Assert.Null(plan.ScalarStorageType);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PreservesPackedUnalignedFieldsAndIgnoresOnlyGenuinelyEmptyValues(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var packed = fixture.Add(5, 1, [(CliTypeIdentity.Primitive("u1", CliValueKind.I4), 0), (integer, 1)], packing: 1);
        var packedSingleton = fixture.Add(4, 1, [(integer, 0)], packing: 1);
        var empty = fixture.Add(1, 1, []);

        var packedPlan = fixture.Planner().Plan(packed, "native-call");
        Assert.Equal(NativeAbiValueKind.IndirectAggregate, packedPlan.Kind);
        Assert.Equal(5, packedPlan.Size);
        Assert.Equal(1, packedPlan.Alignment);
        Assert.Equal(NativeAbiValueKind.ScalarizedAggregate, fixture.Planner().Plan(packedSingleton, "native-call").Kind);
        var emptyPlan = fixture.Planner().Plan(empty, "native-call");
        Assert.Equal(NativeAbiValueKind.IgnoredAggregate, emptyPlan.Kind);
        Assert.Equal(1, emptyPlan.Size);
        Assert.Null(emptyPlan.PhysicalType);
        Assert.Null(emptyPlan.ScalarStorageType);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SubstitutesClosedFieldsSkipsStaticsAndUsesEnumStorage(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var generic = fixture.Add(4, 4, [(CliTypeIdentity.GenericParameter(false, 0), 0)], genericArgument: integer);
        var enumType = fixture.AddEnum(integer);
        var enumSingleton = fixture.Add(4, 4, [(enumType, 0)]);
        fixture.AddStaticField(enumSingleton, CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference));

        var genericPlan = fixture.Planner().Plan(generic, "native-call");
        Assert.Equal(NativeAbiValueKind.ScalarizedAggregate, genericPlan.Kind);
        Assert.Equal(integer, genericPlan.ScalarStorageType);
        Assert.Equal(NativeAbiValueKind.ScalarizedAggregate, fixture.Planner().Plan(enumType, "native-call").Kind);
        Assert.Equal(integer, fixture.Planner().Plan(enumSingleton, "native-call").PhysicalType);
        Assert.Contains(fixture.FieldRequests, field => field.DeclaringType == generic && field.FieldType == integer);
        Assert.DoesNotContain(fixture.FieldRequests, field => field.Definition.IsStatic);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ScalarRepresentedEnumRemainsScalarAndEveryQualifiedPackPreservesSingletonStorage(WasmTarget target)
    {
        var fixture = new Fixture(target);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var enumType = fixture.AddEnum(integer, scalarRepresentation: true);

        var plan = fixture.Planner().Plan(enumType, "native-call");

        Assert.Equal(NativeAbiValueKind.Scalar, plan.Kind);
        Assert.Equal(enumType, plan.LogicalType);
        Assert.Equal(integer, plan.PhysicalType);
        Assert.Equal(integer, plan.ScalarStorageType);
        Assert.Equal(4, plan.Size);
        Assert.Equal(4, plan.Alignment);
        foreach (var packing in new[] { 0, 1, 2, 4, 8, 16, 32, 64, 128 })
        {
            var type = fixture.Add(4, Math.Min(packing == 0 ? 4 : packing, 4), [(integer, 0)], packing: packing);
            var packed = fixture.Planner().Plan(type, "native-call");
            Assert.Equal(NativeAbiValueKind.ScalarizedAggregate, packed.Kind);
            Assert.Equal(integer, packed.PhysicalType);
            Assert.Equal(4, packed.Size);
            Assert.Equal(fixture.GetValueLayout(type).Alignment, packed.Alignment);
        }
    }

    [Fact]
    public void RejectsUnsupportedRootShapesBeforeResolvingMetadata()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        foreach (var type in new[]
        {
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            CliTypeIdentity.Named(Fixture.Assembly, "Fixture", "Reference", false),
            CliTypeIdentity.GenericInstantiation(CliTypeIdentity.Named(Fixture.Assembly, "Fixture", "Open`1", true),
                [CliTypeIdentity.GenericParameter(false, 0)]),
        })
            Reject(fixture, type);
        Assert.Empty(fixture.TypeRequests);
    }

    [Fact]
    public void RejectsUnsupportedMetadataStorageAndRecursiveFieldShapes()
    {
        var fixture = new Fixture(WasmTarget.Wasm64);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        foreach (var type in new[]
        {
            fixture.Add(4, 4, [(integer, 0)], CliTypeLayoutKind.Auto),
            fixture.Add(4, 4, [(integer, 0)], packing: 3),
            fixture.Add(4, 4, [(integer, 0)], declaredSize: -1),
            fixture.Add(4, 4, [(integer, 0)], inlineArrayLength: -1),
            fixture.Add(4, 4, [], inlineArrayLength: 1),
            fixture.Add(4, 4, [(integer, 0), (integer, 0)], inlineArrayLength: 1),
            fixture.Add(4, 4, [(integer, -1)]),
            fixture.Add(4, 4, [(integer, 1)]),
            fixture.Add(4, 4, [(integer, 4)]),
            fixture.Add(4, 4, [(integer, 0)], inlineArrayLength: int.MaxValue),
            fixture.Add(4, 2, [(integer, 0)]),
            fixture.Add(4, 4, [(CliTypeIdentity.Primitive("bool", CliValueKind.I4), 0)]),
            fixture.Add(4, 4, [(CliTypeIdentity.Primitive("char", CliValueKind.I4), 0)]),
            fixture.Add(8, 8, [(CliTypeIdentity.ManagedByReference(integer), 0)]),
            fixture.Add(8, 8, [(CliTypeIdentity.UnmanagedPointer(CliTypeIdentity.GenericParameter(false, 0)), 0)]),
            fixture.Add(4, 4, [(CliTypeIdentity.GenericParameter(false, 0), 0)]),
            fixture.Add(4, 4, [(fixture.Add(1, 1, []), 0)]),
        })
            Reject(fixture, type);
        var recursive = fixture.Add(4, 4, [(integer, 0)]);
        fixture.ReplaceFieldType(recursive, recursive);
        Reject(fixture, recursive);
        foreach (var invalid in new[]
        {
            new ValueLayout(integer, 4, 4, [0]),
            new ValueLayout(integer, 4, 4, []) { ByReferenceOffsets = [0] },
            new ValueLayout(integer, 0, 1, []),
            new ValueLayout(integer, 4, 0, []),
            new ValueLayout(integer, 4, 3, []),
            new ValueLayout(integer, 32, 32, []),
            new ValueLayout(integer, 5, 4, []),
        })
        {
            var type = fixture.Add(4, 4, [(integer, 0)]);
            fixture.Values[type] = invalid with { Type = type };
            Reject(fixture, type);
        }
    }

    [Fact]
    public void RequiredDependenciesAndOperationInputsReject()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        Assert.Throws<ArgumentNullException>(() => new NativeAggregateAbiPlanner(null!, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new NativeAggregateAbiPlanner(fixture, null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new NativeAggregateAbiPlanner(fixture, fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new NativeAggregateAbiPlanner(fixture, fixture, fixture, null!));
        Assert.Throws<ArgumentNullException>(() => fixture.Planner().Plan(null!, "call"));
        var type = fixture.Add(1, 1, []);
        Assert.Throws<ArgumentNullException>(() => fixture.Planner().Plan(type, null!));
        Assert.Throws<ArgumentException>(() => fixture.Planner().Plan(type, " "));
    }

    private static void Reject(Fixture fixture, CliTypeIdentity type)
    {
        var error = Assert.Throws<CompilerException>(() => fixture.Planner().Plan(type, "native-call"));
        Assert.Equal(DiagnosticCode.NativeInterop, error.Diagnostic.Code);
        Assert.Equal("native-call", error.Diagnostic.Method);
    }

    private sealed class Fixture(WasmTarget target) : ITypeDefinitionResolver, IFieldRepository,
        IValueLayoutProvider, IInstanceFieldLayoutProvider
    {
        public static AssemblyIdentity Assembly { get; } = new("NativeFixture");
        private readonly WasmTargetLayout _target = WasmTargetLayout.For(target);
        private readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> _types = [];
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields = [];
        private readonly Dictionary<EntityKey, int> _offsets = [];
        private int _next = 1;
        public Dictionary<CliTypeIdentity, ValueLayout> Values { get; } = [];
        public List<CliTypeIdentity> TypeRequests { get; } = [];
        public List<FieldInstanceModel> FieldRequests { get; } = [];

        public INativeAggregateAbiPlanner Planner() =>
            Assert.IsAssignableFrom<INativeAggregateAbiPlanner>(new NativeAggregateAbiPlanner(this, this, this, this));

        public CliTypeIdentity Add(int size, int alignment, (CliTypeIdentity Type, int Offset)[] fields,
            CliTypeLayoutKind kind = CliTypeLayoutKind.Sequential, int packing = 0,
            int declaredSize = 0, int inlineArrayLength = 0, CliTypeIdentity? genericArgument = null)
        {
            var key = new EntityKey(Assembly, _next++);
            var name = "Aggregate" + key.MetadataToken;
            var type = CliTypeIdentity.Named(Assembly, "Fixture", name, true);
            if (genericArgument is not null)
                type = CliTypeIdentity.GenericInstantiation(type, [genericArgument]);
            var fieldKeys = ImmutableArray.CreateBuilder<EntityKey>();
            foreach (var (fieldType, offset) in fields)
            {
                var fieldKey = new EntityKey(Assembly, _next++);
                _fields.Add(fieldKey, new(fieldKey, key, "Field" + fieldKey.MetadataToken, fieldType.StackKind, false)
                { SignatureType = fieldType });
                _offsets.Add(fieldKey, offset);
                fieldKeys.Add(fieldKey);
            }
            _types.Add(type, new(key, "Fixture", name, true, fieldKeys.ToImmutable(), [])
            { LayoutKind = kind, PackingSize = packing, DeclaredSize = declaredSize, InlineArrayLength = inlineArrayLength });
            Values.Add(type, new(type, size, alignment, []));
            return type;
        }

        public CliTypeIdentity AddEnum(CliTypeIdentity underlying, bool scalarRepresentation = false)
        {
            var storage = GetValueLayout(underlying);
            var type = Add(storage.Size, storage.Alignment, []);
            _types[type] = _types[type] with { IsEnum = true, EnumUnderlyingType = underlying };
            return scalarRepresentation
                ? CliTypeIdentity.Named(Assembly, "Fixture", type.FullName!["Fixture.".Length..], true, underlying.StackKind)
                : type;
        }

        public void AddStaticField(CliTypeIdentity type, CliTypeIdentity fieldType)
        {
            var definition = _types[type];
            var key = new EntityKey(Assembly, _next++);
            _fields.Add(key, new(key, definition.Key, "Static", fieldType.StackKind, true) { SignatureType = fieldType });
            _types[type] = definition with { Fields = definition.Fields.Add(key) };
        }

        public void ReplaceFieldType(CliTypeIdentity type, CliTypeIdentity fieldType)
        {
            var key = Assert.Single(_types[type].Fields);
            _fields[key] = _fields[key] with { SignatureType = fieldType };
        }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            TypeRequests.Add(identity);
            return _types[identity];
        }

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public ValueLayout GetValueLayout(CliTypeIdentity type) => Values.TryGetValue(type, out var layout)
            ? layout : new(type, _target.GetStorageSize(type), _target.GetStorageAlignment(type), []);

        public FieldLayout GetFieldLayout(FieldInstanceModel field)
        {
            FieldRequests.Add(field);
            return new(_offsets[field.Definition.Key]) { Type = field.FieldType, Size = GetValueLayout(field.FieldType).Size };
        }

        public FieldLayout GetFieldLayout(EntityKey field) => throw new InvalidOperationException();
    }
}
