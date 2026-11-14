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
    public void ArrayCreationUsesExplicitSeedsOnlyAndIsDemandDriven()
    {
        var fixture = new Fixture();
        var planner = fixture.CreatePlanner();
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var array = CliTypeIdentity.SzArray(integer);
        var generated = CliTypeIdentity.SzArray(array);
        var absent = planner.Plan([fixture.Method(null)], [integer, array, generated], [integer, array]);
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, absent);
        var method = fixture.Method(RuntimeIntrinsic.ArrayCreateInstance);
        var plan = planner.Plan([method], [integer, array, generated], [integer, array]);
        AssertTypes([integer, array, generated], plan.RuntimeTypes);
        AssertTypes([array, generated], plan.AllocatedTypes);
        Assert.DoesNotContain(CliTypeIdentity.SzArray(generated), plan.RuntimeTypes);
        var repeated = planner.Plan([method], plan.RuntimeTypes, [integer, array]);
        Assert.Equal<CliTypeIdentity>(plan.RuntimeTypes, repeated.RuntimeTypes);
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, planner.Plan([method], [integer]));
    }

    [Fact]
    public void RectangularCreationRootsRanksTwoAndThreeFromSeedsOnDemand()
    {
        var fixture = new Fixture();
        var planner = fixture.CreatePlanner();
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var vector = CliTypeIdentity.SzArray(integer);
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty,
            planner.Plan([fixture.Method(RuntimeIntrinsic.ArrayCreateInstance)], [integer], []));

        var plan = planner.Plan([fixture.Method(RuntimeIntrinsic.ArrayCreateRectangularInstance)],
            [integer], [integer]);

        AssertTypes([integer, CliTypeIdentity.Array(integer, 2), CliTypeIdentity.Array(integer, 3)],
            plan.RuntimeTypes);
        AssertTypes([CliTypeIdentity.Array(integer, 2), CliTypeIdentity.Array(integer, 3)], plan.AllocatedTypes);
        Assert.DoesNotContain(vector, plan.AllocatedTypes);
        Assert.Empty(plan.InitializedTypes);
    }

    [Fact]
    public void ClassConstructorDemandInitializesClosedNamedHandleSeedsOnly()
    {
        var fixture = new Fixture();
        var planner = fixture.CreatePlanner();
        var named = CliTypeIdentity.Named(Fixture.Assembly, "Fixtures", "Registrant", isValueType: false);
        var definition = fixture.Enum("Generic`1", genericArity: 1);
        var closed = fixture.Close(definition, CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var open = CliTypeIdentity.GenericInstantiation(definition, [CliTypeIdentity.GenericParameter(false, 0)]);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        ImmutableArray<CliTypeIdentity> seeds = [named, closed, open, integer,
            CliTypeIdentity.SzArray(named), named];
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty,
            planner.Plan([fixture.Method(null)], [named], typeHandleSeeds: seeds));

        var plan = planner.Plan([fixture.Method(RuntimeIntrinsic.RunClassConstructor)], [named],
            typeHandleSeeds: seeds);

        AssertTypes([closed, named], plan.InitializedTypes);
        Assert.Empty(plan.RuntimeTypes);
        Assert.Empty(plan.AllocatedTypes);
        Assert.Empty(fixture.DefinitionRequests);
    }

    [Fact]
    public void UninitializedObjectDemandAdmitsCreatableNamedAndAllocatedTypes()
    {
        var fixture = new Fixture();
        var plain = fixture.Type("Plain");
        var abstractType = fixture.Type("Abstract", isAbstract: true);
        var interfaceType = fixture.Type("Interface", isInterface: true);
        var byRefLike = fixture.Type("RefStruct", isValue: true, byRefLike: true);
        var value = fixture.Type("Value", isValue: true);
        var generic = fixture.Type("Generic`1", genericArity: 1);
        var closed = fixture.Close(generic, plain);
        var integer = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var nullable = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(new AssemblyIdentity("NetWasm.CoreLib"), "System", "Nullable`1", isValueType: true),
            [integer]);
        ImmutableArray<CliTypeIdentity> seeds = [plain, abstractType, interfaceType, byRefLike, generic, closed,
            text, nullable, CliTypeIdentity.SzArray(plain), CliTypeIdentity.FromStackKind(CliValueKind.Void),
            fixture.Close(generic, CliTypeIdentity.GenericParameter(method: false, 0))];
        var planner = fixture.CreatePlanner();
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, planner.Plan([fixture.Method(null)], [], [], seeds, [value]));

        var plan = planner.Plan([fixture.Method(RuntimeIntrinsic.AllocateUninitializedObject)], [], [], seeds, [value]);

        AssertTypes([plain, closed, integer, value], plan.UninitializedObjectTypes);
        Assert.Empty(plan.StructMarshallingTypes);
        Assert.Empty(plan.RuntimeTypes);
        Assert.Empty(plan.AllocatedTypes);
    }

    [Theory]
    [InlineData(RuntimeIntrinsic.StructMarshallingData, false)]
    [InlineData(RuntimeIntrinsic.StructMarshallingFieldNames, true)]
    public void StructureMarshallingDemandAdmitsNonGenericNamedAndAllocatedValueTypes(
        RuntimeIntrinsic intrinsic,
        bool fieldNames)
    {
        var fixture = new Fixture();
        var plain = fixture.Type("Plain");
        var value = fixture.Type("Value", isValue: true);
        var boxed = fixture.Type("Boxed", isValue: true);
        var allocatedClass = fixture.Type("Allocated");
        var generic = fixture.Type("Generic`1", isValue: true, genericArity: 1);
        var closed = fixture.Close(generic, value);
        var integer = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

        var plan = fixture.CreatePlanner().Plan([fixture.Method(intrinsic)], [], [],
            [plain, value, closed, integer, text, value], [boxed, allocatedClass]);

        AssertTypes([boxed, plain, value, integer], plan.StructMarshallingTypes);
        Assert.Equal(fieldNames, plan.StructMarshallingFieldNames);
        Assert.Empty(plan.UninitializedObjectTypes);
    }

    [Fact]
    public void ArrayReadsRetainBoxesForReachableRectangularValueElements()
    {
        var fixture = new Fixture();
        var integerType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var stringType = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

        var plan = fixture.CreatePlanner().Plan([fixture.Method(RuntimeIntrinsic.ArrayGetValue)],
            [CliTypeIdentity.Array(integerType, 2), CliTypeIdentity.Array(stringType, 3)]);

        AssertTypes([integerType], plan.RuntimeTypes);
        AssertTypes([integerType], plan.AllocatedTypes);
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
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(null!, nullableTypes, fixture, new ArrayCreationRootPlanner(fixture)));
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(fixture, null!, fixture, new ArrayCreationRootPlanner(fixture)));
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(fixture, nullableTypes, null!, new ArrayCreationRootPlanner(fixture)));
        Assert.Throws<ArgumentNullException>(() => new RuntimeIntrinsicTypeRootPlanner(fixture, nullableTypes, fixture, null!));
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
            ThroughContract(new RuntimeIntrinsicTypeRootPlanner(this, new NullableTypeResolver(), this, new ArrayCreationRootPlanner(this)));

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

        internal CliTypeIdentity Type(string name, bool isValue = false, bool isAbstract = false,
            bool isInterface = false, bool byRefLike = false, int genericArity = 0)
        {
            var definition = new TypeDefinitionModel(new(Assembly, 0x02000001 + Definitions.Count),
                "Fixtures", name, isValue, [], [])
            {
                IsAbstract = isAbstract || isInterface,
                IsInterface = isInterface,
                IsByRefLike = byRefLike,
                GenericArity = genericArity,
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
