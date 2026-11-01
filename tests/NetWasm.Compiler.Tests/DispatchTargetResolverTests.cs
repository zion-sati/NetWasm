using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests;

// The method a virtual or interface call runs on each receiver, as ECMA-335
// II.10.3 and II.12.2 define it, over a small type system that gives each type
// its base type, the interfaces it declares, its methods and its MethodImpls.
public sealed class DispatchTargetResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");
    private static readonly CliTypeIdentity String = CliTypeIdentity.Named(Assembly, "System", "String", false);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.Named(Assembly, "System", "Object", false);

    [Fact]
    public void AnOverrideReplacesTheSlotsBody()
    {
        var program = new Program();
        var a = program.Type("A");
        var b = program.Type("B", a);
        var slot = program.Method(a, "M", newSlot: true);
        var overriding = program.Method(b, "M");

        AssertRuns(overriding, program, slot, b);
        AssertRuns(slot, program, slot, a);
    }

    [Fact]
    public void ANewSlotHidesTheSlotFromTheOverridesBelowIt()
    {
        var program = new Program();
        var a = program.Type("A");
        var b = program.Type("B", a);
        var c = program.Type("C", b);
        var first = program.Method(a, "M", newSlot: true);
        var hiding = program.Method(b, "M", newSlot: true);
        var overriding = program.Method(c, "M");

        AssertRuns(first, program, first, c);
        AssertRuns(overriding, program, hiding, c);
    }

    [Fact]
    public void OnlyAVirtualMethodOfTheSlotsSignatureOverridesIt()
    {
        var program = new Program();
        var a = program.Type("A");
        var b = program.Type("B", a);
        var slot = program.Method(a, "M", newSlot: true);
        program.Method(b, "M", isVirtual: false);
        program.Method(b, "M", returns: String);
        program.Method(b, "M", parameters: [String]);
        program.Method(b, "Other");

        AssertRuns(slot, program, slot, b);
        // Only virtual methods of the slot's name are instantiated to compare
        // their signatures.
        Assert.Equal(["M", "M"], program.Instantiated);
    }

    [Fact]
    public void AMethodImplReplacesTheSlotItNamesEvenBelowANewSlot()
    {
        var program = new Program();
        var a = program.Type("A");
        var b = program.Type("B", a);
        var c = program.Type("C", b);
        var slot = program.Method(a, "M", newSlot: true);
        program.Method(b, "M", newSlot: true);
        var named = program.Method(c, "Replacement", newSlot: true);
        program.Implements(c, named, slot);

        AssertRuns(named, program, slot, c);
    }

    [Fact]
    public void AMethodImplThatReusesTheSlotLetsALaterMethodImplNameIt()
    {
        var program = new Program();
        var a = program.Type("A");
        var b = program.Type("B", a);
        var c = program.Type("C", b);
        var slot = program.Method(a, "M", newSlot: true);
        var reusing = program.Method(b, "Reusing");
        program.Implements(b, reusing, slot);
        var last = program.Method(c, "Last", newSlot: true);
        program.Implements(c, last, reusing);

        AssertRuns(last, program, slot, c);
    }

    // C# marks a covariant override PreserveBaseOverrides and names only the
    // method directly above it; without the attribute the MethodImpl replaces
    // only that method's own slot.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACovariantOverrideReplacesTheSlotsItsBaseReplacedOnlyWhenPreserving(bool preserves)
    {
        var program = new Program();
        var a = program.Type("A");
        var b = program.Type("B", a);
        var c = program.Type("C", b);
        var slot = program.Method(a, "Get", newSlot: true, returns: Object);
        var middle = program.Method(b, "Get", newSlot: true, returns: b, preserves: true);
        program.Implements(b, middle, slot);
        var last = program.Method(c, "Get", newSlot: true, returns: c, preserves: preserves);
        program.Implements(c, last, middle);

        AssertRuns(preserves ? last : middle, program, slot, c);
        AssertRuns(last, program, middle, c);
    }

    [Fact]
    public void ACallOnAnArrayRunsItsBaseTypesBody()
    {
        var program = new Program();
        var objectType = program.Type("Object");
        var array = CliTypeIdentity.SzArray(String);
        program.Base(array, objectType);
        var slot = program.Method(objectType, "ToString", newSlot: true, returns: String);

        AssertRuns(slot, program, slot, array);
    }

    // The relationship check comes first; a receiver whose base types never
    // reach the slot's type runs the slot.
    [Fact]
    public void AReceiverOutsideTheSlotsHierarchyRunsTheSlot()
    {
        var program = new Program();
        var a = program.Type("A");
        var unrelated = program.Type("Unrelated");
        var slot = program.Method(a, "M", newSlot: true);
        program.Compatible(unrelated, a);

        AssertRuns(slot, program, slot, unrelated);
    }

    [Fact]
    public void CoincidingOverloadsOverrideTheSlotTheyDeclare()
    {
        var program = new Program();
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var baseDefinition = program.Type("Base`1", arity: 1);
        var derivedDefinition = program.Type("Derived`1", arity: 1);
        program.Base(
            CliTypeIdentity.GenericInstantiation(derivedDefinition, [parameter]),
            CliTypeIdentity.GenericInstantiation(baseDefinition, [parameter]));
        var baseOfInt = CliTypeIdentity.GenericInstantiation(baseDefinition, [int32]);
        var derivedOfInt = CliTypeIdentity.GenericInstantiation(derivedDefinition, [int32]);
        program.Base(derivedOfInt, baseOfInt);
        var generic = program.Method(baseDefinition, "M", newSlot: true, parameters: [parameter]);
        var concrete = program.Method(baseDefinition, "M", newSlot: true, parameters: [int32]);
        var overridesGeneric = program.Method(derivedDefinition, "M", parameters: [parameter]);
        var overridesConcrete = program.Method(derivedDefinition, "M", parameters: [int32]);

        AssertRuns(program.On(overridesGeneric, derivedOfInt), program, program.On(generic, baseOfInt), derivedOfInt);
        AssertRuns(program.On(overridesConcrete, derivedOfInt), program, program.On(concrete, baseOfInt), derivedOfInt);
    }

    [Fact]
    public void CoincidingOverloadsFindTheSlotThroughAnIntermediateBase()
    {
        var program = new Program();
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var baseDefinition = program.Type("Base`1", arity: 1);
        var middleDefinition = program.Type("Middle`1", arity: 1);
        var derivedDefinition = program.Type("Derived`1", arity: 1);
        program.Base(
            CliTypeIdentity.GenericInstantiation(derivedDefinition, [parameter]),
            CliTypeIdentity.GenericInstantiation(middleDefinition, [parameter]));
        program.Base(
            CliTypeIdentity.GenericInstantiation(middleDefinition, [parameter]),
            CliTypeIdentity.GenericInstantiation(baseDefinition, [parameter]));
        var baseOfInt = CliTypeIdentity.GenericInstantiation(baseDefinition, [int32]);
        var middleOfInt = CliTypeIdentity.GenericInstantiation(middleDefinition, [int32]);
        var derivedOfInt = CliTypeIdentity.GenericInstantiation(derivedDefinition, [int32]);
        program.Base(derivedOfInt, middleOfInt);
        program.Base(middleOfInt, baseOfInt);
        var generic = program.Method(baseDefinition, "M", newSlot: true, parameters: [parameter]);
        program.Method(baseDefinition, "M", newSlot: true, parameters: [int32]);
        var overridesGeneric = program.Method(derivedDefinition, "M", parameters: [parameter]);
        program.Method(derivedDefinition, "M", parameters: [int32]);

        AssertRuns(
            program.On(overridesGeneric, derivedOfInt),
            program,
            program.On(generic, baseOfInt),
            derivedOfInt);
    }

    [Fact]
    public void CoincidingOverloadsOverrideANonGenericBasesSlot()
    {
        var program = new Program();
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var baseType = program.Type("Base");
        var derivedDefinition = program.Type("Derived`1", arity: 1);
        program.Base(CliTypeIdentity.GenericInstantiation(derivedDefinition, [parameter]), baseType);
        var derivedOfInt = CliTypeIdentity.GenericInstantiation(derivedDefinition, [int32]);
        program.Base(derivedOfInt, baseType);
        var slot = program.Method(baseType, "M", newSlot: true, parameters: [int32]);
        program.Method(derivedDefinition, "M", newSlot: true, parameters: [parameter]);
        var overriding = program.Method(derivedDefinition, "M", parameters: [int32]);

        AssertRuns(program.On(overriding, derivedOfInt), program, slot, derivedOfInt);
    }

    // Box<T>.N(T) and N(int) coincide once T is int; N(T) implements
    // IShape<T>.N as Box<T> declares it.
    [Fact]
    public void CoincidingOverloadsImplementTheInterfaceMethodTheyDeclare()
    {
        var program = new Program();
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var shape = program.Type("IShape`1", isInterface: true, arity: 1);
        var shapeOfParameter = CliTypeIdentity.GenericInstantiation(shape, [parameter]);
        var shapeOfInt = CliTypeIdentity.GenericInstantiation(shape, [int32]);
        var n = program.Method(shape, "N", newSlot: true, isAbstract: true, parameters: [parameter]);
        var box = program.Type("Box`1", arity: 1, interfaces: [shapeOfParameter]);
        var implements = program.Method(box, "N", newSlot: true, parameters: [parameter]);
        program.Method(box, "N", newSlot: true, parameters: [int32]);
        var boxOfInt = CliTypeIdentity.GenericInstantiation(box, [int32]);

        AssertRuns(program.On(implements, boxOfInt), program, program.On(n, shapeOfInt), boxOfInt);
    }

    // Coinciding overloads with a base type list that never reaches the slot's
    // type keep the slot's body.
    [Fact]
    public void CoincidingOverloadsWithoutTheSlotsBaseKeepTheSlot()
    {
        var program = new Program();
        var int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var baseType = program.Type("Base");
        var derivedDefinition = program.Type("Derived`1", arity: 1);
        var derivedOfInt = CliTypeIdentity.GenericInstantiation(derivedDefinition, [int32]);
        program.Base(derivedOfInt, baseType);
        var slot = program.Method(baseType, "M", newSlot: true, parameters: [int32]);
        program.Method(derivedDefinition, "M", parameters: [parameter]);
        program.Method(derivedDefinition, "M", parameters: [int32]);

        AssertRuns(slot, program, slot, derivedOfInt);
    }

    [Fact]
    public void AnInterfaceMapsToTheMostDerivedTypeThatDeclaresIt()
    {
        var program = new Program();
        var contract = program.Type("I", isInterface: true);
        var method = program.Method(contract, "M", newSlot: true, isAbstract: true);
        var b = program.Type("B", interfaces: [contract]);
        var implemented = program.Method(b, "M", newSlot: true);
        var hides = program.Type("Hides", b);
        program.Method(hides, "M", isVirtual: false);
        var redeclares = program.Type("Redeclares", b, interfaces: [contract]);
        var redeclared = program.Method(redeclares, "M", newSlot: true);

        AssertRuns(implemented, program, method, hides);
        AssertRuns(redeclared, program, method, redeclares);
    }

    [Fact]
    public void AnInterfaceSlotRunsItsOverrideOnTheReceiver()
    {
        var program = new Program();
        var contract = program.Type("I", isInterface: true);
        var method = program.Method(contract, "M", newSlot: true, isAbstract: true);
        var slotHolder = program.Type("SlotHolder");
        program.Method(slotHolder, "M", newSlot: true);
        var declares = program.Type("Declares", slotHolder, interfaces: [contract]);
        var overrides = program.Type("Overrides", declares);
        var overriding = program.Method(overrides, "M");

        AssertRuns(overriding, program, method, overrides);
    }

    [Fact]
    public void ANonPublicVirtualMethodDoesNotImplementAnInterface()
    {
        var program = new Program();
        var contract = program.Type("I", isInterface: true);
        var method = program.Method(contract, "M", newSlot: true, isAbstract: true);
        var type = program.Type("Hidden", interfaces: [contract]);
        program.Method(type, "M", newSlot: true, isPublic: false);

        Assert.Null(new Resolver(program).Resolve(method, type));
    }

    [Fact]
    public void AnExplicitImplementationComesBeforeAPublicMethod()
    {
        var program = new Program();
        var contract = program.Type("I", isInterface: true);
        var method = program.Method(contract, "M", newSlot: true, isAbstract: true);
        var type = program.Type("Explicit", interfaces: [contract]);
        program.Method(type, "M", newSlot: true);
        var body = program.Method(type, "I.M", newSlot: true, isPublic: false);
        program.Implements(type, body, method);
        var derived = program.Type("Derived", type);
        program.Method(derived, "M", isVirtual: false);

        AssertRuns(body, program, method, derived);
    }

    // IOutput<object>.Get through IOutput<string>: the implementation's
    // signature is the method's as the declared interface gives it. An exact
    // implementation anywhere comes before a variant one.
    [Fact]
    public void AVariantInterfaceMapsThroughItsDeclaredInstantiation()
    {
        var program = new Program();
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var output = program.Type("IOutput`1", isInterface: true, arity: 1);
        var outputOfObject = CliTypeIdentity.GenericInstantiation(output, [Object]);
        var outputOfString = CliTypeIdentity.GenericInstantiation(output, [String]);
        program.Variant(outputOfString, outputOfObject);
        var get = program.Method(output, "Get", newSlot: true, isAbstract: true, returns: parameter);
        var producer = program.Type("Producer", interfaces: [outputOfString]);
        var produces = program.Method(producer, "Get", newSlot: true, returns: String);
        var both = program.Type("Both", producer, interfaces: [outputOfObject]);
        var exact = program.Method(both, "Get", newSlot: true, returns: Object);

        AssertRuns(produces, program, program.On(get, outputOfObject), producer);
        AssertRuns(exact, program, program.On(get, outputOfObject), both);
    }

    [Fact]
    public void AVariantExplicitImplementationMapsTheInterface()
    {
        var program = new Program();
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var output = program.Type("IOutput`1", isInterface: true, arity: 1);
        var outputOfObject = CliTypeIdentity.GenericInstantiation(output, [Object]);
        var outputOfString = CliTypeIdentity.GenericInstantiation(output, [String]);
        program.Variant(outputOfString, outputOfObject);
        var get = program.Method(output, "Get", newSlot: true, isAbstract: true, returns: parameter);
        var producer = program.Type("Producer", interfaces: [outputOfString]);
        var body = program.Method(producer, "IOutput.Get", newSlot: true, isPublic: false, returns: String);
        program.Implements(producer, body, program.On(get, outputOfString));

        AssertRuns(body, program, program.On(get, outputOfObject), producer);
    }

    [Fact]
    public void ADefaultMethodRunsWhenNoClassImplementsTheInterface()
    {
        var program = new Program();
        var contract = program.Type("I1", isInterface: true);
        var method = program.Method(contract, "M", newSlot: true);
        var type = program.Type("UsesDefault", interfaces: [contract]);

        AssertRuns(method, program, method, type);
    }

    [Fact]
    public void TheMostSpecificDefaultImplementationRuns()
    {
        var program = new Program();
        var first = program.Type("I1", isInterface: true);
        var method = program.Method(first, "M", newSlot: true);
        var second = program.Type("I2", isInterface: true, interfaces: [first]);
        var secondBody = program.Method(second, "I1.M", isPublic: false);
        program.Implements(second, secondBody, method);
        var third = program.Type("I3", isInterface: true, interfaces: [second, first]);
        var thirdBody = program.Method(third, "I1.M", isPublic: false);
        program.Implements(third, thirdBody, method);
        var usesSecond = program.Type("UsesSecond", interfaces: [second, first]);
        var usesThird = program.Type("UsesThird", interfaces: [third, second, first]);
        var inherits = program.Type("Inherits", usesSecond);

        AssertRuns(secondBody, program, method, usesSecond);
        AssertRuns(thirdBody, program, method, usesThird);
        AssertRuns(secondBody, program, method, inherits);
    }

    [Fact]
    public void AVariantDefaultImplementationRuns()
    {
        var program = new Program();
        var parameter = CliTypeIdentity.GenericParameter(method: false, 0);
        var output = program.Type("IOutput`1", isInterface: true, arity: 1);
        var outputOfObject = CliTypeIdentity.GenericInstantiation(output, [Object]);
        var outputOfString = CliTypeIdentity.GenericInstantiation(output, [String]);
        program.Variant(outputOfString, outputOfObject);
        var get = program.Method(output, "Get", newSlot: true, isAbstract: true, returns: parameter);
        var refined = program.Type("IRefined", isInterface: true, interfaces: [outputOfString]);
        var body = program.Method(refined, "IOutput.Get", isPublic: false, returns: String);
        program.Implements(refined, body, program.On(get, outputOfString));
        var type = program.Type("UsesRefined", interfaces: [refined, outputOfString]);

        AssertRuns(body, program, program.On(get, outputOfObject), type);
    }

    // Two unrelated interfaces that each override the method leave no most
    // specific implementation, and an abstract method without one has none.
    [Fact]
    public void AnAmbiguousOrAbstractInterfaceMethodHasNoTarget()
    {
        var program = new Program();
        var first = program.Type("I1", isInterface: true);
        var method = program.Method(first, "M", newSlot: true);
        var left = program.Type("ILeft", isInterface: true, interfaces: [first]);
        program.Implements(left, program.Method(left, "I1.M", isPublic: false), method);
        var right = program.Type("IRight", isInterface: true, interfaces: [first]);
        program.Implements(right, program.Method(right, "I1.M", isPublic: false), method);
        var diamond = program.Type("Diamond", interfaces: [left, right, first]);
        var abstractContract = program.Type("IAbstract", isInterface: true);
        var abstractMethod = program.Method(abstractContract, "M", newSlot: true, isAbstract: true);
        var none = program.Type("None", interfaces: [abstractContract]);

        Assert.Null(new Resolver(program).Resolve(method, diamond));
        Assert.Null(new Resolver(program).Resolve(abstractMethod, none));
    }

    [Fact]
    public void AnArrayReceiverReachesItsInterfacesThroughItsBaseTypes()
    {
        var program = new Program();
        var contract = program.Type("IDefault", isInterface: true);
        var method = program.Method(contract, "M", newSlot: true);
        var arrayBase = program.Type("Array", interfaces: [contract]);
        var array = CliTypeIdentity.SzArray(String);
        program.Base(array, arrayBase);

        AssertRuns(method, program, method, array);
    }

    private static void AssertRuns(
        MethodInstanceModel expected,
        Program program,
        MethodInstanceModel declaration,
        CliTypeIdentity receiver)
    {
        var target = new Resolver(program).Resolve(declaration, receiver);
        Assert.NotNull(target);
        Assert.Equal(receiver, target.ReceiverType);
        Assert.Equal(expected.Definition.Key, target.Method.Definition.Key);
        Assert.Equal(expected.DeclaringType, target.Method.DeclaringType);
    }

    private sealed class Resolver(Program program) : IDispatchTargetResolver
    {
        private readonly DispatchTargetResolver _resolver = new(
            new NoTypes(), program, program, program, program, program, new Names(), program, program);

        public DispatchTargetModel? Resolve(MethodInstanceModel declaration, CliTypeIdentity receiver) =>
            _resolver.Resolve(declaration, receiver);
    }

    private sealed class NoTypes : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => throw new InvalidOperationException(fullName);
    }

    private sealed class Names : ISymbolFormatter
    {
        public string Format(EntityKey key) => key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }

    // Each type's base type, declared interfaces, methods and MethodImpls; a
    // method instantiates on its declaring type's type arguments.
    private sealed class Program :
        ITypeDefinitionResolver,
        IMethodRepository,
        IMethodImplementationResolver,
        IImplementedInterfaceResolver,
        IBaseTypeResolver,
        IMethodInstanceResolver,
        ITypeRelationshipClassifier
    {
        private sealed record Entry(EntityKey Key, string Name, bool IsInterface, int Arity)
        {
            public List<EntityKey> Methods { get; } = [];

            public List<CliTypeIdentity> Interfaces { get; } = [];

            public List<MethodImplementationInstanceModel> Implementations { get; } = [];
        }

        private readonly Dictionary<CliTypeIdentity, Entry> _entries = [];
        private readonly Dictionary<CliTypeIdentity, CliTypeIdentity> _bases = [];
        private readonly Dictionary<EntityKey, MethodDefinitionModel> _methods = [];
        private readonly Dictionary<EntityKey, CliTypeIdentity> _owners = [];
        private readonly HashSet<(CliTypeIdentity, CliTypeIdentity)> _variant = [];
        private readonly HashSet<(CliTypeIdentity, CliTypeIdentity)> _compatible = [];
        private int _nextType = 0x02000001;
        private int _nextMethod = 0x06000001;

        public List<string> Instantiated { get; } = [];

        public CliTypeIdentity Type(
            string name,
            CliTypeIdentity? baseType = null,
            bool isInterface = false,
            int arity = 0,
            CliTypeIdentity[]? interfaces = null)
        {
            var type = CliTypeIdentity.Named(Assembly, "Tests", name, false);
            var entry = new Entry(new EntityKey(Assembly, _nextType++), name, isInterface, arity);
            entry.Interfaces.AddRange(interfaces ?? []);
            _entries.Add(type, entry);
            if (baseType is not null)
            {
                _bases.Add(type, baseType);
            }
            return type;
        }

        public void Base(CliTypeIdentity type, CliTypeIdentity baseType) => _bases[type] = baseType;

        public void Variant(CliTypeIdentity candidate, CliTypeIdentity target) => _variant.Add((candidate, target));

        public void Compatible(CliTypeIdentity candidate, CliTypeIdentity target) => _compatible.Add((candidate, target));

        public MethodInstanceModel Method(
            CliTypeIdentity type,
            string name,
            bool isVirtual = true,
            bool newSlot = false,
            bool isPublic = true,
            bool isAbstract = false,
            bool preserves = false,
            CliTypeIdentity? returns = null,
            CliTypeIdentity[]? parameters = null)
        {
            var entry = _entries[type];
            var definition = new MethodDefinitionModel(
                new EntityKey(Assembly, _nextMethod++),
                entry.Key,
                name,
                false,
                new MethodSignatureModel(
                    returns ?? CliTypeIdentity.FromStackKind(CliValueKind.Void),
                    [.. parameters ?? []]),
                isAbstract ? 0 : 1)
            {
                IsVirtual = isVirtual,
                IsNewSlot = newSlot,
                IsPublic = isPublic,
                IsAbstract = isAbstract,
                PreservesBaseOverrides = preserves,
            };
            entry.Methods.Add(definition.Key);
            _methods.Add(definition.Key, definition);
            _owners.Add(definition.Key, type);
            return new(definition, type, [], definition.Signature);
        }

        public MethodInstanceModel On(MethodInstanceModel method, CliTypeIdentity type) =>
            Instance(method.Definition, type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : []);

        public void Implements(CliTypeIdentity type, MethodInstanceModel body, MethodInstanceModel declaration) =>
            _entries[type].Implementations.Add(new(body, declaration));

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            var entry = _entries[Definition(identity)];
            return new TypeDefinitionModel(entry.Key, "Tests", entry.Name, false, [], [.. entry.Methods])
            {
                IsInterface = entry.IsInterface,
                GenericArity = entry.Arity,
            };
        }

        public MethodDefinitionModel GetMethod(EntityKey key) => _methods[key];

        public ImmutableArray<MethodImplementationInstanceModel> GetMethodImplementations(CliTypeIdentity type) =>
            [.. _entries[Definition(type)].Implementations];

        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) =>
            [.. _entries[Definition(type)].Interfaces.Select(implemented =>
                type.Shape == CliTypeShape.GenericInstantiation ? implemented.Substitute(type.TypeArguments) : implemented)];

        public CliTypeIdentity? Resolve(CliTypeIdentity type) => _bases.GetValueOrDefault(type);

        public MethodInstanceModel ResolveMethodInstance(
            AssemblyIdentity source,
            int metadataToken,
            string methodDisplayName,
            int ilOffset,
            CliGenericContext? genericContext = null)
        {
            Instantiated.Add(methodDisplayName);
            return Instance(_methods[new EntityKey(source, metadataToken)], genericContext?.TypeArguments ?? []);
        }

        TypeRelationship ITypeRelationshipClassifier.Classify(CliTypeIdentity candidate, CliTypeIdentity target) =>
            new(_variant.Contains((candidate, target))
                ? TypeRelationshipCharacteristics.Hierarchy | TypeRelationshipCharacteristics.GenericVariance
                : Reaches(candidate, target) || _compatible.Contains((candidate, target))
                    ? TypeRelationshipCharacteristics.Hierarchy
                    : TypeRelationshipCharacteristics.None);

        private bool Reaches(CliTypeIdentity candidate, CliTypeIdentity target)
        {
            for (var type = (CliTypeIdentity?)candidate; type is not null; type = Resolve(type))
            {
                if (type.Equals(target) || type.Shape is not (CliTypeShape.SzArray or CliTypeShape.Array) &&
                    GetInterfaces(type).Any(implemented =>
                        implemented.Equals(target) || _variant.Contains((implemented, target))))
                {
                    return true;
                }
            }
            return false;
        }

        private MethodInstanceModel Instance(MethodDefinitionModel definition, ImmutableArray<CliTypeIdentity> typeArguments)
        {
            var owner = _owners[definition.Key];
            return new(
                definition,
                typeArguments.IsDefaultOrEmpty ? owner : CliTypeIdentity.GenericInstantiation(owner, typeArguments),
                [],
                typeArguments.IsDefaultOrEmpty ? definition.Signature : definition.Signature.Substitute(typeArguments));
        }

        private static CliTypeIdentity Definition(CliTypeIdentity identity) =>
            identity.Shape == CliTypeShape.GenericInstantiation ? identity.ElementType! : identity;
    }
}
