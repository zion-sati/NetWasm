// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StructResultClassifierTests
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int16 = CliTypeIdentity.Primitive("i2", CliValueKind.I4);
    private static readonly CliTypeIdentity Boolean = CliTypeIdentity.Primitive("bool", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Double = CliTypeIdentity.Primitive("f8", CliValueKind.F8);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);

    // ValueTask<int>'s shape: a reference, an int, a short and a bool, each its own result in offset order.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void AValueTaskShapeReturnsEachFieldInOffsetOrder(WasmTarget target)
    {
        var fixture = new Fixture(target);
        // Managed storage, as the compiler's layouts give it: one byte and two.
        fixture.Values[Boolean] = new(Boolean, 1, 1, []);
        fixture.Values[Int16] = new(Int16, 2, 2, []);
        var pointer = target == WasmTarget.Wasm32 ? 4 : 8;
        var type = fixture.Add([(Int32, pointer), (Object, 0), (Boolean, pointer + 6), (Int16, pointer + 4)]);

        var shape = Assert.IsType<StructResultShape>(fixture.Classifier().Classify(type));

        Assert.Equal(type, shape.Type);
        Assert.Equal(
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4],
            shape.Results.AsEnumerable());
        Assert.Equal([0, pointer, pointer + 4, pointer + 6], shape.Fields.Select(static field => field.Offset));
        Assert.Equal([pointer, 4, 2, 1], shape.Fields.Select(static field => field.Size));
        Assert.Equal([Object, Int32, Int16, Boolean], shape.Fields.Select(static field => field.Type));
    }

    // A nested value type's fields flatten into the outer value's offsets; a byref field returns as an address and a
    // v128 field as one value.
    [Fact]
    public void NestedValueTypesFlattenAndByRefAndVectorFieldsAreScalars()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var inner = fixture.Add([(Int32, 0), (Int32, 4)]);
        var outer = fixture.Add([(Int64, 8), (inner, 0)]);
        var span = fixture.Add([(CliTypeIdentity.ManagedByReference(Int32), 0), (Int32, 4)]);
        var vector = CliTypeIdentity.FromStackKind(CliValueKind.V128);
        fixture.Values[vector] = new(vector, 16, 16, []);
        var withVector = fixture.Add([(vector, 0), (Double, 16)]);
        var classifier = fixture.Classifier();

        var nested = Assert.IsType<StructResultShape>(classifier.Classify(outer));
        Assert.Equal([0, 4, 8], nested.Fields.Select(static field => field.Offset));
        Assert.Equal([CliValueKind.I4, CliValueKind.I4, CliValueKind.I8], nested.Results.AsEnumerable());
        Assert.Equal(
            [CliValueKind.ManagedAddress, CliValueKind.I4],
            Assert.IsType<StructResultShape>(classifier.Classify(span)).Results.AsEnumerable());
        Assert.Equal(
            [CliValueKind.V128, CliValueKind.F8],
            Assert.IsType<StructResultShape>(classifier.Classify(withVector)).Results.AsEnumerable());
    }

    // A value with bytes its fields do not cover, or cover twice, keeps the buffer, as does one of more than four
    // scalars, counted through its nested values, and one with no field. Static fields are not part of the value.
    [Fact]
    public void ValuesTheirFieldsDoNotCarryKeepTheBuffer()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var classifier = fixture.Classifier();
        var four = fixture.Add([(Int32, 0), (Int32, 4), (Int32, 8), (Int32, 12)]);
        fixture.AddStaticField(four, Int64);
        Assert.Equal(4, Assert.IsType<StructResultShape>(classifier.Classify(four)).Fields.Length);

        var pair = fixture.Add([(Int32, 0), (Int32, 4)]);
        var fiveThroughNesting = fixture.Add([(pair, 0), (pair, 8), (Int32, 16)]);
        Assert.Null(classifier.Classify(fixture.Add([(Int32, 0), (Int32, 4), (Int32, 8), (Int32, 12), (Int32, 16)])));
        Assert.Null(classifier.Classify(fiveThroughNesting));
        Assert.Null(classifier.Classify(fixture.Add([(Int32, 0)], kind: CliTypeLayoutKind.Explicit)));
        Assert.Null(classifier.Classify(fixture.Add([(Int32, 0)], declaredSize: 16)));
        Assert.Null(classifier.Classify(fixture.Add([(Int32, 0)], inlineArrayLength: 4)));
        Assert.Null(classifier.Classify(fixture.Add([])));
        Assert.Null(classifier.Classify(fixture.Add([(Int64, 0), (Int32, 4)])));
        Assert.Null(classifier.Classify(fixture.Add([(fixture.Add([(Int32, 0)], declaredSize: 8), 0), (Int32, 8)])));
        Assert.Null(classifier.Classify(fixture.Add(
            [(CliTypeIdentity.Modified(Int32, CliTypeIdentity.Named(Fixture.Assembly, "System.Runtime.CompilerServices", "IsVolatile", false), true), 0)])));
    }

    [Fact]
    public void OnlyValueTypesAreClassified()
    {
        var classifier = new Fixture(WasmTarget.Wasm32).Classifier();

        Assert.Null(classifier.Classify(Int32));
        Assert.Null(classifier.Classify(Object));
        Assert.Null(classifier.Classify(CliTypeIdentity.FromStackKind(CliValueKind.V128)));
    }

    // The classification is the type's alone: asked again it is the same shape, and its type is resolved once.
    [Fact]
    public void AClassificationIsComputedOncePerType()
    {
        var fixture = new Fixture(WasmTarget.Wasm32);
        var type = fixture.Add([(Int32, 0), (Object, 4)]);
        var classifier = fixture.Classifier();

        var first = classifier.Classify(type);
        var second = classifier.Classify(type);

        Assert.Same(first, second);
        Assert.Equal(1, fixture.TypeRequests.Count(request => request.Equals(type)));
    }

    private sealed class Fixture(WasmTarget target) : ITypeDefinitionResolver, IFieldRepository, IInstanceFieldLayoutProvider
    {
        public static AssemblyIdentity Assembly { get; } = new("StructResultFixture");
        private readonly WasmTargetLayout _target = WasmTargetLayout.For(target);
        private readonly Dictionary<CliTypeIdentity, TypeDefinitionModel> _types = [];
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields = [];
        private readonly Dictionary<EntityKey, int> _offsets = [];
        private int _next = 1;
        public Dictionary<CliTypeIdentity, ValueLayout> Values { get; } = [];
        public List<CliTypeIdentity> TypeRequests { get; } = [];

        public IStructResultClassifier Classifier() =>
            Assert.IsAssignableFrom<IStructResultClassifier>(new StructResultClassifier(this, this, this));

        public CliTypeIdentity Add((CliTypeIdentity Type, int Offset)[] fields,
            CliTypeLayoutKind kind = CliTypeLayoutKind.Sequential, int declaredSize = 0, int inlineArrayLength = 0)
        {
            var key = new EntityKey(Assembly, _next++);
            var name = "Value" + key.MetadataToken;
            var type = CliTypeIdentity.Named(Assembly, "Fixture", name, true);
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
            { LayoutKind = kind, DeclaredSize = declaredSize, InlineArrayLength = inlineArrayLength });
            return type;
        }

        public void AddStaticField(CliTypeIdentity type, CliTypeIdentity fieldType)
        {
            var definition = _types[type];
            var key = new EntityKey(Assembly, _next++);
            _fields.Add(key, new(key, definition.Key, "Static", fieldType.StackKind, true) { SignatureType = fieldType });
            _types[type] = definition with { Fields = definition.Fields.Add(key) };
        }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            TypeRequests.Add(identity);
            return _types[identity];
        }

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            new(_offsets[field.Definition.Key]) { Type = field.FieldType, Size = Size(field.FieldType) };

        public FieldLayout GetFieldLayout(EntityKey field) => new(_offsets[field]);

        private int Size(CliTypeIdentity type) => Values.TryGetValue(type, out var layout)
            ? layout.Size
            : type.StackKind == CliValueKind.ValueType ? 0 : _target.GetStorageSize(type);
    }
}
