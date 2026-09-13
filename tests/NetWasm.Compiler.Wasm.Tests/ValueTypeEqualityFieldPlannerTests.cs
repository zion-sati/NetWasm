using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ValueTypeEqualityFieldPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("PlannerTests");
    private static readonly CliTypeIdentity I4 =
        CliTypeIdentity.Primitive("i4", CliValueKind.I4);

    [Fact]
    public void PlansNestedValueAndReferenceLeavesWithoutPadding()
    {
        var nested = Type("Nested");
        var text = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Text",
            isValueType: false);
        var nestedField = Field(1, "Value", I4);
        var numberField = Field(2, "Number", I4);
        var valueField = Field(3, "Nested", nested);
        var textField = Field(4, "Text", text);
        var fixture = new Fixture(
            [Definition("Nested", [nestedField]),
             Definition("Root", [numberField, valueField, textField])],
            [nestedField, numberField, valueField, textField],
            [(nestedField.Key, 0), (numberField.Key, 0),
             (valueField.Key, 4), (textField.Key, 8)]);

        var result = fixture.Planner.Plan(Type("Root"));

        Assert.Equal(
            [new ValueTypeEqualityField(0, I4),
             new ValueTypeEqualityField(4, I4),
             new ValueTypeEqualityField(8, text)],
            result.ToArray());
    }

    [Fact]
    public void SubstitutesConstructedGenericAndExpandsInlineArray()
    {
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var genericDefinition = Type("Container`1");
        var genericField = Field(5, "Value", parameter);
        var inlineField = Field(6, "Element", I4);
        var fixture = new Fixture(
            [Definition("Container`1", [genericField]),
             Definition("Inline", [inlineField]) with { InlineArrayLength = 3 }],
            [genericField, inlineField],
            [(genericField.Key, 0), (inlineField.Key, 0)]);

        var constructed = CliTypeIdentity.GenericInstantiation(
            genericDefinition,
            [I4]);

        Assert.Equal(
            [new ValueTypeEqualityField(0, I4)],
            fixture.Planner.Plan(constructed).ToArray());
        Assert.Equal(
            [new ValueTypeEqualityField(0, I4),
             new ValueTypeEqualityField(4, I4),
             new ValueTypeEqualityField(8, I4)],
            fixture.Planner.Plan(Type("Inline")).ToArray());
    }

    [Fact]
    public void EmptyValueHasNoLeavesAndRecursiveValueIsRejected()
    {
        var recursive = Type("Recursive");
        var recursiveField = Field(7, "Self", recursive);
        var fixture = new Fixture(
            [Definition("Empty", []), Definition("Recursive", [recursiveField])],
            [recursiveField],
            [(recursiveField.Key, 0)]);

        Assert.Empty(fixture.Planner.Plan(Type("Empty")));
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Planner.Plan(recursive));
    }

    [Fact]
    public void TreatsManagedAddressesAndPointersAsAtomicLeaves()
    {
        var fixture = new Fixture([], [], []);
        var managedAddress = CliTypeIdentity.ManagedByReference(I4);
        var pointer = CliTypeIdentity.UnmanagedPointer(I4);

        Assert.Equal(
            [new ValueTypeEqualityField(0, managedAddress)],
            fixture.Planner.Plan(managedAddress).ToArray());
        Assert.Equal(
            [new ValueTypeEqualityField(0, pointer)],
            fixture.Planner.Plan(pointer).ToArray());
    }

    private static CliTypeIdentity Type(string name) =>
        CliTypeIdentity.Named(Assembly, "Tests", name, isValueType: true);

    private static EntityKey Key(int token) => new(Assembly, token);

    private static FieldDefinitionModel Field(
        int token,
        string name,
        CliTypeIdentity type) => new(
        Key(token),
        Key(100 + token),
        name,
        type,
        isStatic: false);

    private static TypeDefinitionModel Definition(
        string name,
        ImmutableArray<FieldDefinitionModel> fields) => new(
        Key(200 + name.Length),
        "Tests",
        name,
        IsValueType: true,
        [.. fields.Select(field => field.Key)],
        []);

    private sealed class Fixture :
        ITypeDefinitionResolver,
        IFieldRepository,
        IInstanceFieldLayoutProvider,
        IValueLayoutProvider
    {
        private readonly Dictionary<string, TypeDefinitionModel> _types;
        private readonly Dictionary<EntityKey, FieldDefinitionModel> _fields;
        private readonly Dictionary<EntityKey, int> _offsets;

        public Fixture(
            IEnumerable<TypeDefinitionModel> types,
            IEnumerable<FieldDefinitionModel> fields,
            IEnumerable<(EntityKey Field, int Offset)> offsets)
        {
            _types = types.ToDictionary(type => type.FullName);
            _fields = fields.ToDictionary(field => field.Key);
            _offsets = offsets.ToDictionary(pair => pair.Field, pair => pair.Offset);
            Planner = new ValueTypeEqualityFieldPlanner(this, this, this, this);
        }

        public ValueTypeEqualityFieldPlanner Planner { get; }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            _types[identity.FullName!];

        public FieldDefinitionModel GetField(EntityKey key) => _fields[key];

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            new(_offsets[field.Definition.Key]);

        public FieldLayout GetFieldLayout(EntityKey field) =>
            new(_offsets[field]);

        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(
            type,
            type.StackKind is CliValueKind.I8 or CliValueKind.F8 ? 8 : 4,
            4,
            []);
    }
}
