using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class PackedValueLayoutResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Packed");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
    private static readonly CliTypeIdentity Type = CliTypeIdentity.Named(Assembly, "Tests", "Value", true);

    [Fact]
    public void ExplicitFieldsPublishTheirDeclaredOverlappingOffsets()
    {
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var first = new FieldDefinitionModel(new(Assembly, 0x04000001), TypeKey, "First", scalar, false)
        { ExplicitOffset = 0 };
        var second = first with { Key = new(Assembly, 0x04000002), Name = "Second" };
        var definition = new TypeDefinitionModel(TypeKey, "Tests", "Value", true, [first.Key, second.Key], [])
        { LayoutKind = CliTypeLayoutKind.Explicit };
        var state = new ManagedValueLayoutState();
        var resolver = Assert.IsAssignableFrom<IValueLayoutResolver>(new ValueLayoutResolver(
            new DefinitionResolver(definition), new Fields([first, second]),
            new ExplicitValueLayoutResolver(), WasmTargetLayout.Wasm32, state, new FieldLayoutFailureBinder()));
        Assert.Equal(4, resolver.Resolve(Type, definition).Size);
        Assert.Equal(state.Fields[first.Key], state.Fields[second.Key]);
        Assert.Equal((0, 4), (state.Fields[first.Key].Offset, state.Fields[first.Key].Size));
    }

    [Theory]
    [InlineData(0, 4, 12)]
    [InlineData(1, 1, 7)]
    [InlineData(2, 2, 8)]
    [InlineData(4, 4, 12)]
    [InlineData(8, 4, 12)]
    [InlineData(16, 4, 12)]
    [InlineData(32, 4, 12)]
    [InlineData(64, 4, 12)]
    [InlineData(128, 4, 12)]
    public void PacksSequentialPrimitiveFields(int pack, int alignment, int size)
    {
        foreach (var target in new[] { WasmTargetLayout.Wasm32, WasmTargetLayout.Wasm64 })
        {
            var (resolver, state, definition) = Create(pack, target,
                CliTypeIdentity.Primitive("u1", CliValueKind.I4),
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                CliTypeIdentity.Primitive("i2", CliValueKind.I4));
            var value = resolver.Resolve(Type, definition);
            Assert.Equal((size, alignment), (value.Size, value.Alignment));
            Assert.Equal(alignment, state.Fields[new(Assembly, 0x04000002)].Offset);
            Assert.Equal(alignment + 4, state.Fields[new(Assembly, 0x04000003)].Offset);
            Assert.Empty(value.ReferenceOffsets);
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PackingPreservesReferenceAlignmentAndArrayStride(WasmTarget target)
    {
        var targetLayout = WasmTargetLayout.For(target);
        var (resolver, state, definition) = Create(1, targetLayout,
            CliTypeIdentity.Primitive("u1", CliValueKind.I4),
            CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference),
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var value = resolver.Resolve(Type, definition);
        Assert.Equal(targetLayout.ObjectReferenceSize * 3, value.Size);
        Assert.Equal(targetLayout.ObjectReferenceAlignment, value.Alignment);
        Assert.Equal(targetLayout.ObjectReferenceSize, Assert.Single(value.ReferenceOffsets));
        Assert.Equal(targetLayout.ObjectReferenceSize, state.Fields[new(Assembly, 0x04000002)].Offset);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CarriesManagedByReferenceIdentityThroughNestedLayout(WasmTarget target)
    {
        var targetLayout = WasmTargetLayout.For(target);
        var (resolver, _, definition) = Create(1, targetLayout,
            CliTypeIdentity.Primitive("u1", CliValueKind.I4),
            CliTypeIdentity.ManagedByReference(CliTypeIdentity.Primitive("i4", CliValueKind.I4)));
        var value = resolver.Resolve(Type, definition);
        Assert.Equal(targetLayout.AddressSize, Assert.Single(value.ByReferenceOffsets));
        Assert.Equal(value.ReferenceOffsets.ToArray(), value.ByReferenceOffsets.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(256)]
    public void InvalidPackingFailsBeforePublishingFields(int pack)
    {
        var (resolver, state, definition) = Create(pack, WasmTargetLayout.Wasm32,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));
        var failure = Assert.Throws<CompilerException>(() => resolver.Resolve(Type, definition));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, failure.Diagnostic.Code);
        Assert.Empty(state.Fields);
        Assert.Empty(state.Values);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(9, false)]
    [InlineData(9, true)]
    public void DeclaredSizeIsAMinimumAndOnlyReferenceStorageRequiresPadding(int declaredSize, bool reference)
    {
        var fieldType = reference
            ? CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference)
            : CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var (resolver, state, definition) = Create(1, WasmTargetLayout.Wasm64, fieldType);
        definition = definition with { DeclaredSize = declaredSize };
        if (declaredSize < 0)
        {
            Assert.Throws<CompilerException>(() => resolver.Resolve(Type, definition));
            Assert.Empty(state.Fields);
            return;
        }
        Assert.Equal(reference ? 16 : Math.Max(4, declaredSize), resolver.Resolve(Type, definition).Size);
    }

    private static (IValueLayoutResolver Resolver, ManagedValueLayoutState State, TypeDefinitionModel Definition)
        Create(int pack, WasmTargetLayout target, params CliTypeIdentity[] fieldTypes)
    {
        var fields = fieldTypes.Select((type, index) => new FieldDefinitionModel(
            new(Assembly, 0x04000001 + index), TypeKey, "Field" + index, type, false)).ToArray();
        var definition = new TypeDefinitionModel(TypeKey, "Tests", "Value", true,
            [.. fields.Select(field => field.Key)], [])
        {
            LayoutKind = CliTypeLayoutKind.Sequential,
            PackingSize = pack,
        };
        var state = new ManagedValueLayoutState();
        return (new ValueLayoutResolver(new DefinitionResolver(definition), new Fields(fields),
            new ExplicitValueLayoutResolver(), target, state, new FieldLayoutFailureBinder()), state, definition);
    }

    private sealed class DefinitionResolver(TypeDefinitionModel definition) : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity type) => definition;
    }

    private sealed class Fields(FieldDefinitionModel[] fields) : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) => fields.Single(field => field.Key == key);
    }
}
