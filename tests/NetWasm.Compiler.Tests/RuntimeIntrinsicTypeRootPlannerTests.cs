using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeIntrinsicTypeRootPlannerTests
{
    [Theory]
    [InlineData(RuntimeIntrinsic.NullableGetUnderlyingType)]
    [InlineData(RuntimeIntrinsic.ArraySetValue)]
    public void NullableInspectionRetainsUnderlyingMetadataWithoutAllocations(RuntimeIntrinsic intrinsic)
    {
        var fixture = new Fixture();
        var underlying = fixture.Enum("State");
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"),
                "System", "Nullable`1", isValueType: true), [underlying]);
        var unrelated = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Fixture.Assembly, "Fixtures", "Box`1", isValueType: false),
            [underlying]);
        var planner = fixture.CreatePlanner();

        var noDemand = planner.Plan([fixture.Method(null)], [nullable, unrelated]);
        var plan = planner.Plan([fixture.Method(intrinsic)],
            [unrelated, nullable, nullable]);

        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, noDemand);
        Assert.Equal(underlying, Assert.Single(plan.RuntimeTypes));
        Assert.Empty(plan.AllocatedTypes);
        Assert.Empty(fixture.DefinitionRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeBasedValuesRetainExactArraysAndBoxElementsOnlyOnDemand(bool boxElements)
    {
        var fixture = new Fixture();
        var first = fixture.Enum("First");
        var second = fixture.Enum("Second");
        var underlying = fixture.Definitions[first].EnumUnderlyingType;
        var methods = new List<MethodInstanceModel> { fixture.Method(RuntimeIntrinsic.EnumGetValues) };
        if (boxElements)
        {
            methods.Add(fixture.Method(RuntimeIntrinsic.ArrayGetValue));
        }
        var planner = fixture.CreatePlanner();

        var plan = planner.Plan(methods, [second, first, first]);
        var reordered = planner.Plan(methods.AsEnumerable().Reverse(), [first, second]);

        CliTypeIdentity[] elements = [first, second, underlying];
        var arrays = elements.Select(CliTypeIdentity.SzArray).ToArray();
        AssertTypes(elements.Concat(arrays), plan.RuntimeTypes);
        AssertTypes(boxElements ? arrays.Concat(elements) : arrays, plan.AllocatedTypes);
        Assert.Equal<CliTypeIdentity>(plan.RuntimeTypes, reordered.RuntimeTypes);
        Assert.Equal<CliTypeIdentity>(plan.AllocatedTypes, reordered.AllocatedTypes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedNestedEnumsRetainSeparateArrayAndBoxIdentities(bool boxElements)
    {
        var fixture = new Fixture();
        var definition = fixture.Enum("Outer`1+Code", genericArity: 1);
        var first = fixture.Close(definition, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var second = fixture.Close(definition, CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference));
        var methods = new List<MethodInstanceModel> { fixture.Method(RuntimeIntrinsic.EnumGetValues) };
        if (boxElements)
        {
            methods.Add(fixture.Method(RuntimeIntrinsic.ArrayGetValue));
        }

        var plan = fixture.CreatePlanner().Plan(methods, [definition, first, second]);

        CliTypeIdentity[] elements = [first, second, fixture.Definitions[definition].EnumUnderlyingType];
        var arrays = elements.Select(CliTypeIdentity.SzArray).ToArray();
        AssertTypes(elements.Concat(arrays), plan.RuntimeTypes);
        AssertTypes(boxElements ? arrays.Concat(elements) : arrays, plan.AllocatedTypes);
        Assert.DoesNotContain(definition, plan.AllocatedTypes);
        Assert.DoesNotContain(CliTypeIdentity.SzArray(definition), plan.RuntimeTypes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenEnumDefinitionsRetainOnlyConcreteUnderlyingArrays(bool boxElements)
    {
        var fixture = new Fixture();
        var definition = fixture.Enum("Outer`1+Code", genericArity: 1);
        var underlying = fixture.Definitions[definition].EnumUnderlyingType;
        var methods = new List<MethodInstanceModel> { fixture.Method(RuntimeIntrinsic.EnumGetValues) };
        if (boxElements)
        {
            methods.Add(fixture.Method(RuntimeIntrinsic.ArrayGetValue));
        }

        var plan = fixture.CreatePlanner().Plan(methods, [definition]);

        var array = CliTypeIdentity.SzArray(underlying);
        AssertTypes([underlying, array], plan.RuntimeTypes);
        AssertTypes(boxElements ? [underlying, array] : [array], plan.AllocatedTypes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GenericValuesRetainOnlyTheRequestedClosedResult(bool underlyingResult, bool boxElements)
    {
        var fixture = new Fixture();
        var definition = fixture.Enum("Outer`1+Code", genericArity: 1);
        var closed = fixture.Close(definition, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var unrelated = fixture.Enum("Unrelated");
        var methods = new List<MethodInstanceModel>
        {
            fixture.Method(RuntimeIntrinsic.EnumGetValues, [closed], underlyingResult),
        };
        if (boxElements)
        {
            methods.Add(fixture.Method(RuntimeIntrinsic.ArrayGetValue));
        }

        var plan = fixture.CreatePlanner().Plan(methods, [definition, closed, unrelated]);

        var element = underlyingResult ? fixture.Definitions[closed].EnumUnderlyingType : closed;
        var array = CliTypeIdentity.SzArray(element);
        AssertTypes([element, array], plan.RuntimeTypes);
        AssertTypes(boxElements ? [element, array] : [array], plan.AllocatedTypes);
        Assert.Equal(closed, Assert.Single(fixture.DefinitionRequests));
    }

    [Fact]
    public void GenericNamedEnumValuesAndNullableInspectionCombineIndependentDemands()
    {
        var fixture = new Fixture();
        var enumType = fixture.Enum("State");
        var underlying = CliTypeIdentity.FromStackKind(CliValueKind.I8);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"),
                "System", "Nullable`1", isValueType: true), [underlying]);

        var plan = fixture.CreatePlanner().Plan(
            [fixture.Method(RuntimeIntrinsic.EnumGetValues, [enumType]),
             fixture.Method(RuntimeIntrinsic.NullableGetUnderlyingType)],
            [enumType, nullable]);

        var array = CliTypeIdentity.SzArray(enumType);
        AssertTypes([enumType, array, underlying], plan.RuntimeTypes);
        Assert.Equal(array, Assert.Single(plan.AllocatedTypes));
    }

    [Fact]
    public void MetadataOnlyEnumOperationsAndArrayReadsDoNotInventEnumArrays()
    {
        var fixture = new Fixture();

        var plan = fixture.CreatePlanner().Plan(
            [fixture.Method(RuntimeIntrinsic.EnumGetName), fixture.Method(RuntimeIntrinsic.ArrayGetValue)],
            [fixture.Enum("State")]);

        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, plan);
        Assert.Empty(fixture.DefinitionRequests);
    }

    [Fact]
    public void NullableArrayReadsRetainUnderlyingBoxesInsteadOfNullableBoxes()
    {
        var fixture = new Fixture();
        var underlying = fixture.Enum("Payload");
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"), "System", "Nullable`1", true), [underlying]);
        var plan = fixture.CreatePlanner().Plan([fixture.Method(RuntimeIntrinsic.ArrayGetValue)],
            [nullable, CliTypeIdentity.SzArray(nullable)]);
        Assert.Equal(underlying, Assert.Single(plan.RuntimeTypes));
        Assert.Equal(underlying, Assert.Single(plan.AllocatedTypes));
    }

    [Fact]
    public void ArrayReadsRetainBoxesForReachableValueElementArraysOnly()
    {
        var fixture = new Fixture();
        var enumType = fixture.Enum("State");
        var integerType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var stringType = CliTypeIdentity.Primitive(
            "string", CliValueKind.ManagedReference, isValueType: false);
        var enumArray = CliTypeIdentity.SzArray(enumType);
        var integerArray = CliTypeIdentity.SzArray(integerType);
        var stringArray = CliTypeIdentity.SzArray(stringType);

        var plan = fixture.CreatePlanner().Plan(
            [fixture.Method(RuntimeIntrinsic.ArrayGetValue)],
            [enumType, enumArray, integerArray, stringArray]);

        AssertTypes([enumType, integerType], plan.RuntimeTypes);
        AssertTypes([enumType, integerType], plan.AllocatedTypes);
        Assert.DoesNotContain(enumArray, plan.RuntimeTypes);
        Assert.DoesNotContain(integerArray, plan.RuntimeTypes);
        Assert.DoesNotContain(stringType, plan.AllocatedTypes);
        Assert.Empty(fixture.DefinitionRequests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EnumNamesRetainTheStringArrayWithoutBoxingReferenceElements(bool generic, bool readElements)
    {
        var fixture = new Fixture();
        var enumType = fixture.Enum("State");
        var methods = new List<MethodInstanceModel>
        {
            fixture.Method(RuntimeIntrinsic.EnumGetNames, generic ? [enumType] : []),
        };
        if (readElements)
        {
            methods.Add(fixture.Method(RuntimeIntrinsic.ArrayGetValue));
        }

        var plan = fixture.CreatePlanner().Plan(methods, [enumType]);

        var element = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var array = CliTypeIdentity.SzArray(element);
        AssertTypes([element, array], plan.RuntimeTypes);
        Assert.Equal(array, Assert.Single(plan.AllocatedTypes));
        Assert.Empty(fixture.DefinitionRequests);
    }

    [Fact]
    public void TypeBasedValuesIgnoreNonEnumsAndOpenConstructedTypes()
    {
        var fixture = new Fixture();
        var nonEnum = fixture.Enum("OrdinaryValue");
        fixture.Definitions[nonEnum] = fixture.Definitions[nonEnum] with { IsEnum = false };
        var definition = fixture.Enum("Outer`1+Code", genericArity: 1);
        var open = fixture.Close(definition, CliTypeIdentity.GenericParameter(method: false, 0));
        var primitive = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var reference = CliTypeIdentity.Named(Fixture.Assembly, "Fixtures", "Reference", isValueType: false);

        var plan = fixture.CreatePlanner().Plan([fixture.Method(RuntimeIntrinsic.EnumGetValues)],
            [nonEnum, open, primitive, reference, CliTypeIdentity.SzArray(nonEnum)]);

        Assert.Empty(plan.RuntimeTypes);
        Assert.Empty(plan.AllocatedTypes);
        Assert.Equal(nonEnum, Assert.Single(fixture.DefinitionRequests));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ToObjectRetainsBoxedClosedEnumsWithoutArrayDemand(bool generic)
    {
        var fixture = new Fixture();
        var definition = fixture.Enum(generic ? "Outer`1+Code" : "Code", genericArity: generic ? 1 : 0);
        var enumType = generic
            ? fixture.Close(definition, CliTypeIdentity.FromStackKind(CliValueKind.I4))
            : definition;

        var plan = fixture.CreatePlanner().Plan([fixture.Method(RuntimeIntrinsic.EnumToObject)], [enumType]);

        Assert.Equal(enumType, Assert.Single(plan.RuntimeTypes));
        Assert.Equal(enumType, Assert.Single(plan.AllocatedTypes));
    }

    [Fact]
    public void ToObjectDoesNotAllocateOpenEnumDefinitions()
    {
        var fixture = new Fixture();
        var definition = fixture.Enum("Outer`1+Code", genericArity: 1);

        var plan = fixture.CreatePlanner().Plan([fixture.Method(RuntimeIntrinsic.EnumToObject)], [definition]);

        Assert.Empty(plan.RuntimeTypes);
        Assert.Empty(plan.AllocatedTypes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void GenericValuesRejectArgumentsWithoutClosedEnumStorage(int invalidShape)
    {
        var fixture = new Fixture();
        var enumType = fixture.Enum("State");
        ImmutableArray<CliTypeIdentity> arguments = invalidShape switch
        {
            0 => [enumType, enumType],
            1 => [CliTypeIdentity.FromStackKind(CliValueKind.I4)],
            _ => [fixture.Enum("Outer`1+Code", genericArity: 1)],
        };
        var method = fixture.Method(RuntimeIntrinsic.EnumGetValues, arguments);

        var exception = Assert.Throws<CompilerException>(() => fixture.CreatePlanner().Plan([method], []));

        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
        Assert.Contains(method.CanonicalName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlannerValidatesItsActorContract()
    {
        var fixture = new Fixture();
        var nullableTypes = new NullableTypeResolver();
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(null!, nullableTypes, fixture));
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(fixture, nullableTypes, null!));
        var planner = fixture.CreatePlanner();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, []));
        Assert.Throws<ArgumentNullException>(() => planner.Plan([], null!));
    }

    private static void AssertTypes(IEnumerable<CliTypeIdentity> expected, ImmutableArray<CliTypeIdentity> actual) =>
        Assert.Equal(expected.OrderBy(type => type.CanonicalName, StringComparer.Ordinal), actual);

    private static IRuntimeIntrinsicTypeRootPlanner ThroughContract(IRuntimeIntrinsicTypeRootPlanner planner) => planner;

    private sealed class Fixture : IRuntimeIntrinsicRegistry, ITypeDefinitionResolver
    {
        internal static readonly AssemblyIdentity Assembly = new("in-memory");
        private readonly Dictionary<EntityKey, RuntimeIntrinsic> _intrinsics = [];
        private int _nextMethod;
        internal Dictionary<CliTypeIdentity, TypeDefinitionModel> Definitions { get; } = [];
        internal List<CliTypeIdentity> DefinitionRequests { get; } = [];

        internal IRuntimeIntrinsicTypeRootPlanner CreatePlanner() =>
            ThroughContract(new RuntimeIntrinsicTypeRootPlanner(this, new NullableTypeResolver(), this));

        internal CliTypeIdentity Enum(string name, int genericArity = 0)
        {
            var definition = new TypeDefinitionModel(new(Assembly, 0x02000001 + Definitions.Count),
                "Fixtures", name, true, [], [])
            {
                IsEnum = true,
                GenericArity = genericArity,
                EnumUnderlyingType = CliTypeIdentity.Primitive("u2", CliValueKind.I4),
            };
            var identity = CliTypeIdentity.FromDefinition(definition);
            Definitions.Add(identity, definition);
            return identity;
        }

        internal CliTypeIdentity Close(CliTypeIdentity definition, CliTypeIdentity argument)
        {
            var identity = CliTypeIdentity.GenericInstantiation(definition, [argument]);
            Definitions.Add(identity, Definitions[definition]);
            return identity;
        }

        internal MethodInstanceModel Method(RuntimeIntrinsic? intrinsic,
            ImmutableArray<CliTypeIdentity> arguments = default, bool underlyingResult = false)
        {
            var definition = new MethodDefinitionModel(new(Assembly, 0x06000001 + _nextMethod++),
                new(Assembly, 0x020000ff), underlyingResult ? "InternalGetValuesAsUnderlyingType" : "InternalGetValues",
                true, MethodSignatureModel.Create(CliValueKind.ManagedReference), 1);
            if (intrinsic is { } value)
            {
                _intrinsics.Add(definition.Key, value);
            }
            return new(definition, CliTypeIdentity.Named(Assembly, "Fixtures", "Owner", isValueType: false),
                arguments.IsDefault ? [] : arguments, definition.Signature);
        }

        public bool TryGetIntrinsic(EntityKey candidate, out RuntimeIntrinsic intrinsic) =>
            _intrinsics.TryGetValue(candidate, out intrinsic);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            DefinitionRequests.Add(identity);
            return Definitions[identity];
        }
    }
}
