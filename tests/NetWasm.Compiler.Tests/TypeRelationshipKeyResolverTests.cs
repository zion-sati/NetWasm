using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class TypeRelationshipKeyResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Tests");
    // NetWasm names System.Object as a primitive, so the fixture's root object is its own type.
    private static readonly CliTypeIdentity Object = Named("Tests", "Object");
    private static readonly CliTypeIdentity ArrayType = Named("System", "Array");
    private static readonly CliTypeIdentity String = Named("Tests", "String");
    private static readonly CliTypeIdentity Value = Named("Tests", "Value", valueType: true);
    private static readonly CliTypeIdentity Animal = Named("Tests", "Animal");
    private static readonly CliTypeIdentity Dog = Named("Tests", "Dog");
    private static readonly CliTypeIdentity Pet = Named("Tests", "IPet");
    private static readonly CliTypeIdentity Producer = Named("Tests", "Producer`1");
    private static readonly CliTypeIdentity Enumerable = Named("System.Collections.Generic", "IEnumerable`1");
    private static readonly CliTypeIdentity Collection = Named("System.Collections.Generic", "ICollection`1");
    private static readonly CliTypeIdentity List = Named("System.Collections.Generic", "IList`1");
    private static readonly CliTypeIdentity ReadOnlyCollection =
        Named("System.Collections.Generic", "IReadOnlyCollection`1");
    private static readonly CliTypeIdentity ReadOnlyList = Named("System.Collections.Generic", "IReadOnlyList`1");
    private static readonly CliTypeIdentity Consumer = Named("Tests", "IConsumer`1");
    private static readonly CliTypeIdentity Kennel = Named("Tests", "Kennel");
    private static readonly CliTypeIdentity Shelter = Named("Tests", "Shelter");
    private static readonly CliTypeIdentity Plain = Named("Tests", "Plain`1");

    [Fact]
    public void KeepsEveryPairTheClassifierFindsAssignmentCompatible()
    {
        var (classifier, keys) = Create();
        CliTypeIdentity[] universe =
        [
            Object, ArrayType, String, Value, Animal, Dog, Pet,
            Of(Enumerable, String), Of(Enumerable, Object), Of(Enumerable, Animal), Of(Enumerable, Dog),
            Of(List, String), Of(List, Object), Of(Collection, Dog), Of(ReadOnlyList, Animal),
            Of(ReadOnlyCollection, Object), Of(Producer, String), Of(Producer, Object), Of(Producer, Value),
            Of(Consumer, Dog), Of(Consumer, Animal), Of(Consumer, Object), Of(ReadOnlyList, Value),
            Of(ReadOnlyList, Object), Of(Collection, Value), CliTypeIdentity.SzArray(Pet),
            CliTypeIdentity.SzArray(String), CliTypeIdentity.SzArray(Object), CliTypeIdentity.SzArray(Dog),
            CliTypeIdentity.SzArray(Animal), CliTypeIdentity.SzArray(Value), CliTypeIdentity.Array(String, 2),
            CliTypeIdentity.Array(Object, 1), CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
        ];
        var compatible = 0;

        foreach (var candidate in universe)
        {
            foreach (var target in universe)
            {
                if (!classifier.Classify(candidate, target).IsAssignmentCompatible)
                {
                    continue;
                }
                compatible++;
                var candidateKeys = keys.Resolve(candidate);
                var targetKeys = keys.Resolve(target);
                Assert.True(
                    candidateKeys.Unbounded || targetKeys.Unbounded ||
                    candidateKeys.Reachable.Contains(targetKeys.Own),
                    $"{candidate} is assignable to {target}, whose key {targetKeys.Own} it does not reach.");
                Assert.True(
                    candidateKeys.Unbounded || targetKeys.Unbounded || targetKeys.ArgumentKey is not { } argument ||
                    candidateKeys.Arguments?.GetValueOrDefault(targetKeys.Own) is not { } admitted ||
                    admitted.Contains(argument),
                    $"{candidate} is assignable to {target}, whose argument key it does not admit.");
            }
        }
        Assert.True(compatible > universe.Length * 2, "The universe should relate more than identical pairs.");
        Assert.DoesNotContain(keys.Resolve(Of(Enumerable, String)).Own, keys.Resolve(Animal).Reachable);
        Assert.False(keys.Resolve(Dog).Unbounded);
    }

    [Fact]
    public void FirstTypeArgumentsNarrowAGenericDefinitionsInstantiations()
    {
        var (_, keys) = Create();
        string Own(CliTypeIdentity type) => keys.Resolve(type).Own;
        IReadOnlySet<string>? Admitted(CliTypeIdentity candidate, CliTypeIdentity definition) =>
            keys.Resolve(candidate).Arguments![Own(Of(definition, Object))];

        // A covariant reference argument admits its hierarchy: Dog's IEnumerable<Dog> reaches IEnumerable<Animal>.
        Assert.Contains(Own(Animal), Admitted(Dog, Enumerable)!);
        Assert.DoesNotContain(Own(String), Admitted(Dog, Enumerable)!);
        // A reference array's element does the same for the array interfaces, even invariant IList<T>.
        Assert.Contains(Own(Object), Admitted(CliTypeIdentity.SzArray(String), List)!);
        Assert.DoesNotContain(Own(Dog), Admitted(CliTypeIdentity.SzArray(String), List)!);
        // A value element converts to other same-sized element types, and a contravariant argument to its subtypes.
        Assert.Null(Admitted(CliTypeIdentity.SzArray(Value), List));
        Assert.Null(Admitted(Dog, Consumer));
        // Array targets are filed by their element: string[] converts to object[], not to Dog[].
        var arrays = keys.Resolve(CliTypeIdentity.SzArray(String)).Arguments![keys.Resolve(CliTypeIdentity.SzArray(Dog)).Own];
        Assert.Contains(keys.Resolve(CliTypeIdentity.SzArray(Object)).ArgumentKey!, arrays!);
        Assert.DoesNotContain(keys.Resolve(CliTypeIdentity.SzArray(Dog)).ArgumentKey!, arrays!);
        // Targets are filed by their first argument's own key.
        Assert.Equal(Own(Dog), keys.Resolve(Of(Enumerable, Dog)).ArgumentKey);
        Assert.Null(keys.Resolve(Dog).ArgumentKey);
    }

    // Two instantiations of one definition admit what either admits; a definition without recorded variance is invariant.
    [Fact]
    public void InstantiationsOfOneDefinitionJoinAndUnrecordedVarianceIsInvariant()
    {
        var (_, keys) = Create();
        string Own(CliTypeIdentity type) => keys.Resolve(type).Own;

        var kennel = keys.Resolve(Kennel).Arguments![Own(Of(Enumerable, Object))]!;
        Assert.Contains(Own(Dog), kennel);
        Assert.Contains(Own(Animal), kennel);
        Assert.Contains(Own(String), kennel);
        Assert.Equal([Own(Dog)], keys.Resolve(Of(Plain, Dog)).Arguments![Own(Of(Plain, Object))]!);
        // An argument the keys cannot bound admits anything, whichever instantiation came first.
        Assert.Null(keys.Resolve(Shelter).Arguments![Own(Of(Enumerable, Object))]);
    }

    [Fact]
    public void LeavesTypesItCannotKeyUnbounded()
    {
        var (_, keys) = Create();

        Assert.True(keys.Resolve(CliTypeIdentity.GenericParameter(method: false, 0)).Unbounded);
        Assert.True(keys.Resolve(Named("Tests", "Missing")).Unbounded);
        Assert.True(keys.Resolve(CliTypeIdentity.ManagedByReference(String)).Unbounded);
        Assert.Same(keys.Resolve(Dog), keys.Resolve(Dog));
        Assert.Throws<ArgumentNullException>(() => keys.Resolve(null!));
        Assert.True(new TypeRelationshipKeyResolver(
            new Finder(), new Definitions(), new Identities(), new Interfaces(), new ThrowingBases())
            .Resolve(Dog).Unbounded);
    }

    [Fact]
    public void FactoryCreatesAResolver() =>
        Assert.IsType<TypeRelationshipKeyResolver>(new TypeRelationshipKeyResolverFactory().Create(
            new Finder(), new Definitions(), new Identities(), new Interfaces(), new Bases()));

    private static (TypeRelationshipClassifier Classifier, TypeRelationshipKeyResolver Keys) Create() =>
        (new TypeRelationshipClassifier(new Finder(), new Definitions(), new Identities(), new Interfaces(), new Bases()),
            new TypeRelationshipKeyResolver(new Finder(), new Definitions(), new Identities(), new Interfaces(), new Bases()));

    private static CliTypeIdentity Named(string ns, string name, bool valueType = false) =>
        CliTypeIdentity.Named(Assembly, ns, name, valueType);

    private static CliTypeIdentity Of(CliTypeIdentity definition, CliTypeIdentity argument) =>
        CliTypeIdentity.GenericInstantiation(definition, [argument]);

    private static readonly ImmutableDictionary<string, TypeDefinitionModel> Models = new[]
    {
        Model(Object, 1), Model(ArrayType, 2), Model(String, 3), Model(Value, 4), Model(Animal, 5), Model(Dog, 6),
        Model(Pet, 7, isInterface: true),
        Model(Producer, 8, variance: CliGenericVariance.Covariant),
        Model(Enumerable, 9, isInterface: true, variance: CliGenericVariance.Covariant),
        Model(Collection, 10, isInterface: true, variance: CliGenericVariance.Invariant),
        Model(List, 11, isInterface: true, variance: CliGenericVariance.Invariant),
        Model(ReadOnlyCollection, 12, isInterface: true, variance: CliGenericVariance.Covariant),
        Model(ReadOnlyList, 13, isInterface: true, variance: CliGenericVariance.Covariant),
        Model(Consumer, 14, isInterface: true, variance: CliGenericVariance.Contravariant),
        Model(Kennel, 15),
        Model(Shelter, 17),
        // A generic definition whose metadata records no variance.
        Model(Plain, 16),
    }.ToImmutableDictionary(model => model.FullName);

    private static TypeDefinitionModel Model(
        CliTypeIdentity identity, int token, bool isInterface = false, CliGenericVariance? variance = null)
    {
        var dot = identity.FullName!.LastIndexOf('.');
        return new(new EntityKey(Assembly, token), identity.FullName[..dot], identity.FullName[(dot + 1)..],
            identity.IsValueType, [], [])
        {
            IsInterface = isInterface,
            GenericArity = variance is null ? 0 : 1,
            GenericParameterVariances = variance is { } value ? [value] : [],
        };
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            var fullName = identity.ElementType?.FullName ?? identity.FullName;
            if (identity.CanonicalName == "primitive:object")
            {
                fullName = "Tests.Object";
            }
            return Models.TryGetValue(fullName ?? string.Empty, out var model)
                ? model
                : throw new KeyNotFoundException(identity.CanonicalName);
        }
    }

    private sealed class Finder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => Models[fullName];
    }

    private sealed class Identities : ITypeIdentityResolver
    {
        public CliTypeIdentity GetTypeIdentity(EntityKey key) =>
            key.MetadataToken == 2 ? ArrayType : throw new KeyNotFoundException(key.ToString());
    }

    private sealed class Interfaces : IImplementedInterfaceResolver
    {
        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) =>
            type == Dog ? [Pet, Of(Enumerable, Dog), Of(Consumer, Animal)] :
            type == Animal ? [Of(ReadOnlyCollection, Animal)] :
            type == Kennel ? [Of(Enumerable, Dog), Of(Enumerable, String)] :
            type == Shelter ? [Of(Enumerable, Named("Tests", "Missing")), Of(Enumerable, Dog)] : [];
    }

    private class Bases : IBaseTypeResolver
    {
        public virtual CliTypeIdentity? Resolve(CliTypeIdentity type) =>
            type == Dog ? Animal :
            type.Shape is CliTypeShape.SzArray or CliTypeShape.Array ? ArrayType :
            type == Object || type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
                Definitions().IsInterface(type) ? null : Object;

        private static Definitions Definitions() => new();
    }

    private sealed class ThrowingBases : Bases
    {
        public override CliTypeIdentity? Resolve(CliTypeIdentity type) => throw new InvalidOperationException("bases");
    }

}

internal static class TestDefinitionsExtensions
{
    public static bool IsInterface(this ITypeDefinitionResolver definitions, CliTypeIdentity type)
    {
        try
        {
            return definitions.ResolveTypeIdentity(type).IsInterface;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }
}
