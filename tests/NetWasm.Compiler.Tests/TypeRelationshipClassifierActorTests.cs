using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class TypeRelationshipClassifierActorTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");
    private static readonly CliTypeIdentity ObjectType = Named("Object");
    private static readonly CliTypeIdentity StringType = Named("String");
    private static readonly CliTypeIdentity ValueType = Named("Value", true);

    [Fact]
    public void GenericVarianceRequiresCompatibleReferenceTypeArguments()
    {
        var covariant = Generic("IOutput`1");
        var contravariant = Generic("IInput`1");
        var invariant = Generic("IInvariant`1");
        var malformed = Generic("IMalformed`1");
        var mixed = Generic("IMixed`2");
        var covariantDelegate = Generic("Producer`1");
        var definitions = new DefinitionResolver(
            Definition(ObjectType, 1),
            Definition(StringType, 2),
            Definition(ValueType, 3),
            Definition(covariant, 10, CliGenericVariance.Covariant),
            Definition(contravariant, 11, CliGenericVariance.Contravariant),
            Definition(invariant, 12, CliGenericVariance.Invariant),
            Definition(malformed, 13),
            Definition(mixed, 14, CliGenericVariance.Invariant, CliGenericVariance.Covariant),
            Definition(covariantDelegate, 15, CliGenericVariance.Covariant));
        var classifier = Create(definitions, new BaseResolver(
            (StringType, ObjectType)));

        Assert.True(classifier.Classify(
            Instantiate(covariant, StringType),
            Instantiate(covariant, ObjectType)).RequiresVariantMethodResolution);
        Assert.True(classifier.Classify(
            Instantiate(contravariant, ObjectType),
            Instantiate(contravariant, StringType)).RequiresVariantMethodResolution);
        Assert.True(classifier.Classify(
            Instantiate(covariant, StringType),
            Instantiate(covariant, StringType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            Instantiate(covariant, ValueType),
            Instantiate(covariant, ObjectType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            Instantiate(covariant, ObjectType),
            Instantiate(covariant, ValueType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            Instantiate(invariant, StringType),
            Instantiate(invariant, ObjectType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            Instantiate(contravariant, StringType),
            Instantiate(contravariant, ObjectType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            Instantiate(malformed, StringType),
            Instantiate(malformed, ObjectType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            Instantiate(covariant, StringType),
            ObjectType).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            ObjectType,
            Instantiate(covariant, StringType)).RequiresVariantMethodResolution);
        Assert.False(classifier.Classify(
            CliTypeIdentity.GenericInstantiation(
                covariant,
                [StringType, ObjectType]),
            Instantiate(covariant, StringType)).RequiresVariantMethodResolution);
        Assert.True(classifier.Classify(
            CliTypeIdentity.GenericInstantiation(mixed, [StringType, StringType]),
            CliTypeIdentity.GenericInstantiation(mixed, [StringType, ObjectType])).RequiresVariantMethodResolution);
        Assert.True(classifier.Classify(
            Instantiate(covariantDelegate, StringType),
            Instantiate(covariantDelegate, ObjectType)).IsAssignmentCompatible);
        Assert.True(classifier.Classify(
            Instantiate(covariant, Instantiate(covariant, StringType)),
            Instantiate(covariant, Instantiate(covariant, ObjectType))).IsAssignmentCompatible);
    }

    [Fact]
    public void OpenGenericParametersAreComparedWithoutDefinitionResolution()
    {
        var invariant = Generic("IInvariant");
        var definitions = new DefinitionResolver(
            Definition(invariant, 1, CliGenericVariance.Invariant));
        var classifier = Create(definitions);
        var openType = Instantiate(
            invariant,
            CliTypeIdentity.GenericParameter(method: false, index: 0));
        var closedType = Instantiate(invariant, Named("Concrete"));

        Assert.False(classifier.Classify(openType, closedType).IsAssignmentCompatible);
        Assert.False(classifier.Classify(closedType, openType).IsAssignmentCompatible);
    }

    [Fact]
    public void ReferenceInterfacesAreAssignableToObjectThroughGenericVariance()
    {
        var systemObject = CliTypeIdentity.Named(
            Assembly,
            "System",
            "Object",
            isValueType: false);
        var serviceContract = Named("IServiceContract");
        var covariantDelegate = Generic("CovariantDelegate");
        var classifier = Create(
            new DefinitionResolver(
                new TypeDefinitionModel(
                    new EntityKey(Assembly, 1),
                    "System",
                    "Object",
                    false,
                    [],
                    []),
                Definition(serviceContract, 2),
                Definition(covariantDelegate, 3, CliGenericVariance.Covariant)),
            new BaseResolver());

        Assert.True(classifier.Classify(serviceContract, systemObject).IsAssignmentCompatible);
        Assert.True(classifier.Classify(
            Instantiate(covariantDelegate, serviceContract),
            Instantiate(covariantDelegate, systemObject)).IsAssignmentCompatible);
    }

    [Fact]
    public void ArrayRelationshipsApplyCliShapeElementAndInterfaceRules()
    {
        var arrayType = Named("Array");
        var interfaces = new[]
        {
            ArrayInterface("IEnumerable`1"),
            ArrayInterface("ICollection`1"),
            ArrayInterface("IList`1"),
            ArrayInterface("IReadOnlyCollection`1"),
            ArrayInterface("IReadOnlyList`1"),
        };
        var unrelatedInterface = ArrayInterface("IUnrelated`1");
        var definitions = new DefinitionResolver(
        [
            Definition(ObjectType, 1),
            Definition(StringType, 2),
            Definition(ValueType, 3),
            Definition(arrayType, 20),
            .. interfaces.Select((identity, index) =>
                ArrayInterfaceDefinition(identity, 21 + index)),
            ArrayInterfaceDefinition(unrelatedInterface, 30),
        ]);
        var classifier = new TypeRelationshipClassifier(
            new ArrayTypeFinder(),
            definitions,
            new ArrayTypeIdentityResolver(),
            new EmptyInterfaceResolver(),
            new BaseResolver((StringType, ObjectType)));
        var strings = CliTypeIdentity.SzArray(StringType);

        foreach (var interfaceType in interfaces)
        {
            var relationship = classifier.Classify(
                strings,
                Instantiate(interfaceType, StringType));
            Assert.True(relationship.IsHierarchyAssignable);
            Assert.True(relationship.IsSzArrayGenericInterface);
        }
        Assert.False(classifier.Classify(
            strings,
            Instantiate(unrelatedInterface, StringType)).IsSzArrayGenericInterface);
        Assert.True(classifier.Classify(
            strings,
            Instantiate(interfaces[0], ObjectType)).IsSzArrayGenericInterface);
        Assert.True(classifier.Classify(strings, CliTypeIdentity.SzArray(StringType)).IsHierarchyAssignable);
        Assert.True(classifier.Classify(strings, CliTypeIdentity.SzArray(ObjectType)).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            strings,
            CliTypeIdentity.SzArray(CliTypeIdentity.SzArray(StringType))).IsHierarchyAssignable);
        Assert.False(classifier.Classify(StringType, CliTypeIdentity.SzArray(StringType)).IsHierarchyAssignable);
        Assert.True(classifier.Classify(
            CliTypeIdentity.Array(StringType, 2),
            CliTypeIdentity.Array(StringType, 2)).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            CliTypeIdentity.Array(StringType, 2),
            CliTypeIdentity.Array(StringType, 3)).IsHierarchyAssignable);
        Assert.True(classifier.Classify(
            strings,
            CliTypeIdentity.Array(StringType, 1)).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            CliTypeIdentity.Array(StringType, 1),
            strings).IsHierarchyAssignable);
        Assert.False(classifier.Classify(strings, ObjectType).IsHierarchyAssignable);
    }

    [Fact]
    public void ArrayRelationshipsUseOnlyCliReducedIntegralElementPairs()
    {
        var classifier = Create(new DefinitionResolver(
            Definition(ObjectType, 1)));
        var i1 = CliTypeIdentity.Primitive("i1", CliValueKind.I4);
        var u1 = CliTypeIdentity.Primitive("u1", CliValueKind.I4);
        var i2 = CliTypeIdentity.Primitive("i2", CliValueKind.I4);
        var u2 = CliTypeIdentity.Primitive("u2", CliValueKind.I4);
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var u4 = CliTypeIdentity.Primitive("u4", CliValueKind.I4);
        var i8 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
        var u8 = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
        var nativeInt = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
        var nativeUInt = CliTypeIdentity.Primitive("nativeuint", CliValueKind.NativeInt);
        var enumType = Named("Number", true).WithStackStorageType(i4);

        Assert.True(IsArrayAssignable(classifier, i1, u1));
        Assert.True(IsArrayAssignable(classifier, i2, u2));
        Assert.True(IsArrayAssignable(classifier, i4, u4));
        Assert.True(IsArrayAssignable(classifier, i8, u8));
        Assert.True(IsArrayAssignable(classifier, nativeInt, nativeUInt));
        Assert.True(IsArrayAssignable(classifier, enumType, i4));
        Assert.False(IsArrayAssignable(
            classifier,
            CliTypeIdentity.Primitive("bool", CliValueKind.I4),
            u1));
        Assert.False(IsArrayAssignable(
            classifier,
            CliTypeIdentity.Primitive("char", CliValueKind.I4),
            u2));
        Assert.False(IsArrayAssignable(
            classifier,
            CliTypeIdentity.Primitive("f4", CliValueKind.F4),
            i4));
    }

    private static bool IsArrayAssignable(
        TypeRelationshipClassifier classifier,
        CliTypeIdentity candidate,
        CliTypeIdentity target) => classifier.Classify(
            CliTypeIdentity.SzArray(candidate),
            CliTypeIdentity.SzArray(target)).IsHierarchyAssignable;

    [Fact]
    public void ReferenceArrayCovarianceRecursesAcrossJaggedAndRankedArrays()
    {
        var marker = Named("Marker");
        var markerInterface = Named("IMarker");
        var definitions = new DefinitionResolver(
            [
                Definition(ObjectType, 1),
                Definition(ValueType, 2),
                Definition(marker, 3),
                Definition(markerInterface, 4)
            ]);
#pragma warning disable CA1859 // Exercise the one-action capability contract directly.
        ITypeRelationshipClassifier classifier = new TypeRelationshipClassifier(
            new ArrayTypeFinder(),
            definitions,
            new ArrayTypeIdentityResolver(),
            new InterfaceResolver((marker, markerInterface)),
            new BaseResolver());
#pragma warning restore CA1859

        var markerArray = CliTypeIdentity.SzArray(marker);
        var interfaceArray = CliTypeIdentity.SzArray(markerInterface);

        Assert.True(classifier.Classify(markerArray, interfaceArray).IsHierarchyAssignable);
        Assert.True(classifier.Classify(
            CliTypeIdentity.SzArray(markerArray),
            CliTypeIdentity.SzArray(interfaceArray)).IsHierarchyAssignable);
        Assert.True(classifier.Classify(
            CliTypeIdentity.Array(marker, 2),
            CliTypeIdentity.Array(markerInterface, 2)).IsHierarchyAssignable);
        Assert.False(classifier.Classify(interfaceArray, markerArray).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            CliTypeIdentity.Array(marker, 2),
            CliTypeIdentity.Array(markerInterface, 3)).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            CliTypeIdentity.SzArray(ValueType),
            CliTypeIdentity.SzArray(ObjectType)).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            CliTypeIdentity.SzArray(ObjectType),
            CliTypeIdentity.SzArray(ValueType)).IsHierarchyAssignable);
        var genericParameterArray = CliTypeIdentity.SzArray(
            CliTypeIdentity.GenericParameter(method: true, index: 0));
        Assert.True(classifier.Classify(genericParameterArray, genericParameterArray).IsHierarchyAssignable);
    }

    [Fact]
    public void InterfaceRelationshipsTraverseInheritanceAndTerminateCycles()
    {
        var target = Generic("ITarget`1");
        var missing = Generic("IMissing`1");
        var inherited = Instantiate(target, StringType);
        var missingInterface = Instantiate(missing, StringType);
        var candidate = Named("Candidate");
        var cycle = Named("Cycle");
        var cyclicCandidate = Named("CyclicCandidate");
        var definitions = new DefinitionResolver(
            Definition(ObjectType, 1),
            Definition(StringType, 2),
            Definition(candidate, 3),
            Definition(cycle, 4),
            Definition(target, 5, CliGenericVariance.Covariant),
            Definition(cyclicCandidate, 6),
            Definition(missing, 7, CliGenericVariance.Covariant));
        var classifier = new TypeRelationshipClassifier(
            new ArrayTypeFinder(),
            definitions,
            new ArrayTypeIdentityResolver(),
            new InterfaceResolver(
                (cycle, inherited),
                (cyclicCandidate, cycle),
                (cycle, cyclicCandidate)),
            new BaseResolver((candidate, cycle)));

        Assert.True(classifier.Classify(candidate, inherited).IsHierarchyAssignable);
        Assert.True(classifier.Classify(cyclicCandidate, inherited).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            cyclicCandidate,
            missingInterface).IsHierarchyAssignable);
    }

    [Fact]
    public void ImplementedInterfacesApplyVarianceBeforeRejectingUnrelatedTargets()
    {
        var output = Generic("IOutput`1");
        var candidate = Named("Candidate");
        var covariantString = Instantiate(output, StringType);
        var covariantObject = Instantiate(output, ObjectType);
        var unrelated = Instantiate(output, ValueType);
        var definitions = new DefinitionResolver(
            Definition(ObjectType, 1),
            Definition(StringType, 2),
            Definition(ValueType, 3),
            Definition(candidate, 4),
            Definition(output, 5, CliGenericVariance.Covariant));
        var classifier = new TypeRelationshipClassifier(
            new ArrayTypeFinder(),
            definitions,
            new ArrayTypeIdentityResolver(),
            new InterfaceResolver((candidate, covariantString)),
            new BaseResolver((StringType, ObjectType)));

        Assert.True(classifier.Classify(
            candidate,
            covariantObject).IsHierarchyAssignable);
        Assert.False(classifier.Classify(
            candidate,
            unrelated).IsHierarchyAssignable);
    }

    [Fact]
    public void RepeatedRelationshipReusesTheCanonicalPairResult()
    {
        var target = Named("ITarget");
        var unrelatedTarget = Named("IUnrelatedTarget");
        var candidate = Named("Candidate");
        var definitions = new DefinitionResolver(Definition(unrelatedTarget, 3),
            Definition(target, 1),
            Definition(candidate, 2));
        var interfaces = new CountingInterfaceResolver(candidate, target);
        var classifier = new TypeRelationshipClassifier(
            new ArrayTypeFinder(),
            definitions,
            new ArrayTypeIdentityResolver(),
            interfaces,
            new BaseResolver());

        Assert.True(classifier.Classify(candidate, target).IsHierarchyAssignable);
        var callsAfterFirstClassification = interfaces.CallCount;
        Assert.True(classifier.Classify(candidate, target).IsHierarchyAssignable);

        Assert.Equal(2, callsAfterFirstClassification);
        Assert.Equal(callsAfterFirstClassification, interfaces.CallCount);
        Assert.False(classifier.Classify(candidate, unrelatedTarget).IsHierarchyAssignable);
        Assert.Equal(callsAfterFirstClassification, interfaces.CallCount);
    }

    private static TypeRelationshipClassifier Create(
        ITypeDefinitionResolver definitions,
        IBaseTypeResolver? bases = null) => new TypeRelationshipClassifier(
            new ArrayTypeFinder(),
            definitions,
            new ArrayTypeIdentityResolver(),
            new EmptyInterfaceResolver(),
            bases ?? new BaseResolver());

    private static CliTypeIdentity Named(string name, bool value = false) =>
        CliTypeIdentity.Named(Assembly, "Tests", name, value);

    private static CliTypeIdentity Generic(string name) =>
        CliTypeIdentity.Named(Assembly, "Tests", name, false);

    private static CliTypeIdentity ArrayInterface(string name) =>
        CliTypeIdentity.Named(
            Assembly,
            "System.Collections.Generic",
            name,
            false);

    private static TypeDefinitionModel ArrayInterfaceDefinition(
        CliTypeIdentity identity,
        int token) => new(
            new EntityKey(Assembly, token),
            "System.Collections.Generic",
            identity.FullName!["System.Collections.Generic.".Length..],
            false,
            [],
            [])
        {
            IsInterface = true,
            GenericArity = 1,
            GenericParameterVariances = [CliGenericVariance.Invariant],
        };

    private static CliTypeIdentity Instantiate(
        CliTypeIdentity definition,
        CliTypeIdentity argument) =>
        CliTypeIdentity.GenericInstantiation(definition, [argument]);

    private static TypeDefinitionModel Definition(
        CliTypeIdentity identity,
        int token,
        params CliGenericVariance[] variances) => new(
            new EntityKey(Assembly, token),
            "Tests",
            identity.FullName!["Tests.".Length..],
            identity.IsValueType,
            [],
            [])
        {
            IsInterface = identity.FullName!["Tests.".Length..].StartsWith('I'),
            GenericArity = variances.Length == 0 &&
                identity.FullName!["Tests.".Length..].StartsWith('I') ? 1 :
                variances.Length,
            GenericParameterVariances = [.. variances],
        };

    private sealed class DefinitionResolver(params TypeDefinitionModel[] definitions) :
        ITypeDefinitionResolver
    {
        private readonly Dictionary<string, TypeDefinitionModel> _definitions =
            definitions.ToDictionary(definition => definition.FullName);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            var fullName = identity.ElementType?.FullName ?? identity.FullName;
            if (fullName is null && identity.CanonicalName == "primitive:object")
            {
                fullName = "System.Object";
            }

            return _definitions[fullName ?? throw new InvalidOperationException(
                $"Test identity '{identity.CanonicalName}' has no metadata full name.")];
        }
    }

    private sealed class BaseResolver(
        params (CliTypeIdentity Type, CliTypeIdentity Base)[] bases) : IBaseTypeResolver
    {
        private readonly Dictionary<CliTypeIdentity, CliTypeIdentity> _bases =
            bases.ToDictionary(pair => pair.Type, pair => pair.Base);

        public CliTypeIdentity? Resolve(CliTypeIdentity type) =>
            _bases.GetValueOrDefault(type);
    }

    private sealed class EmptyInterfaceResolver : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) => [];
    }

    private sealed class InterfaceResolver(
        params (CliTypeIdentity Type, CliTypeIdentity Interface)[] mappings) :
        IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) =>
            [.. mappings
                .Where(mapping => mapping.Type == type)
                .Select(mapping => mapping.Interface)];
    }

    private sealed class CountingInterfaceResolver(
        CliTypeIdentity candidate,
        CliTypeIdentity target) : IImplementedInterfaceResolver
    {
        public int CallCount { get; private set; }

        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type)
        {
            CallCount++;
            return type.Equals(candidate) ? [target] : [];
        }
    }

    private sealed class ArrayTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) =>
            Definition(Named("Array"), 20);
    }

    private sealed class ArrayTypeIdentityResolver : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey key) => Named("Array");
    }
}
