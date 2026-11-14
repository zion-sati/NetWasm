using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayCreationRootPlannerTests
{
    [Fact]
    public void ExplicitSeedsRetainExactArraysWithoutRecursivelyExpandingThem()
    {
        var definitions = new Definitions();
        var planner = Assert.IsAssignableFrom<IArrayCreationRootPlanner>(new ArrayCreationRootPlanner(definitions));
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var text = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
        var array = CliTypeIdentity.SzArray(text);
        var rectangular = CliTypeIdentity.Array(integer, 2);
        var pointer = CliTypeIdentity.UnmanagedPointer(integer);
        ImmutableArray<CliTypeIdentity> seeds = [integer, text, array, rectangular, pointer, integer];

        var first = planner.Plan(seeds);
        var repeated = planner.Plan(seeds);
        var reordered = planner.Plan([.. seeds.Reverse()]);
        var expected = seeds.Distinct().Select(CliTypeIdentity.SzArray)
            .OrderBy(type => type.CanonicalName, StringComparer.Ordinal).ToArray();

        Assert.Equal<CliTypeIdentity>(expected, first.AllocatedTypes);
        Assert.Equal<CliTypeIdentity>(first.RuntimeTypes, repeated.RuntimeTypes);
        Assert.Equal<CliTypeIdentity>(first.AllocatedTypes, reordered.AllocatedTypes);
        Assert.DoesNotContain(CliTypeIdentity.SzArray(CliTypeIdentity.SzArray(array)), first.RuntimeTypes);
        Assert.Equal(seeds.Distinct().Concat(expected).Distinct().Count(), first.RuntimeTypes.Length);
        Assert.Empty(definitions.Requests);
    }

    [Fact]
    public void ClosedReferenceAndValueTypesAreEligibleIncludingInterfaceAndAbstractTypes()
    {
        var definitions = new Definitions();
        var reference = definitions.Add("Reference");
        var iface = definitions.Add("Interface", isInterface: true);
        var abstractType = definitions.Add("Abstract", isAbstract: true);
        var value = definitions.Add("Value", isValue: true);
        var generic = definitions.Add("Generic`1", arity: 1);
        var closed = CliTypeIdentity.GenericInstantiation(generic, [value]);
        definitions.Items.Add(closed, definitions.Items[generic]);
        var planner = Assert.IsAssignableFrom<IArrayCreationRootPlanner>(new ArrayCreationRootPlanner(definitions));

        var plan = planner.Plan([reference, iface, abstractType, value, closed]);

        Assert.Equal(5, plan.AllocatedTypes.Length);
        foreach (var element in new[] { reference, iface, abstractType, value, closed })
        {
            Assert.Contains(CliTypeIdentity.SzArray(element), plan.AllocatedTypes);
            Assert.Contains(element, plan.RuntimeTypes);
            Assert.DoesNotContain(element, plan.AllocatedTypes);
        }
    }

    [Fact]
    public void InvalidElementsDoNotAcquireArrayRoots()
    {
        var definitions = new Definitions();
        var open = definitions.Add("Open`1", arity: 1);
        var byrefLike = definitions.Add("RefStruct", isValue: true, byrefLike: true);
        var parameter = CliTypeIdentity.GenericParameter(false, 0);
        var openInstance = CliTypeIdentity.GenericInstantiation(open, [parameter]);
        var byref = CliTypeIdentity.ManagedByReference(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var planner = Assert.IsAssignableFrom<IArrayCreationRootPlanner>(new ArrayCreationRootPlanner(definitions));

        var plan = planner.Plan([open, byrefLike, parameter, openInstance, byref,
            CliTypeIdentity.FromStackKind(CliValueKind.Void)]);

        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, plan);
        Assert.Equal(2, definitions.Requests.Count);
        Assert.Contains(open, definitions.Requests);
        Assert.Contains(byrefLike, definitions.Requests);
    }

    [Fact]
    public void RectangularPlansRootEachRequestedRankForEligibleElementsOnly()
    {
        var definitions = new Definitions();
        var value = definitions.Add("Value", isValue: true);
        var byrefLike = definitions.Add("RefStruct", isValue: true, byrefLike: true);
        var integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var planner = Assert.IsAssignableFrom<IArrayCreationRootPlanner>(new ArrayCreationRootPlanner(definitions));

        var plan = planner.Plan([integer, value, byrefLike, integer], [2, 3]);

        Assert.Equal<CliTypeIdentity>(
            new[] { CliTypeIdentity.Array(integer, 2), CliTypeIdentity.Array(integer, 3),
                CliTypeIdentity.Array(value, 2), CliTypeIdentity.Array(value, 3) }
                .OrderBy(type => type.CanonicalName, StringComparer.Ordinal),
            plan.AllocatedTypes);
        Assert.Contains(integer, plan.RuntimeTypes);
        Assert.Contains(value, plan.RuntimeTypes);
        Assert.DoesNotContain(byrefLike, plan.RuntimeTypes);
        Assert.DoesNotContain(CliTypeIdentity.SzArray(integer), plan.AllocatedTypes);
        Assert.Throws<ArgumentException>(() => planner.Plan([integer], [1]));
        Assert.Throws<ArgumentException>(() => planner.Plan([integer], default));
        Assert.Throws<ArgumentException>(() => planner.Plan(default, [2]));
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, planner.Plan([], [2]));
    }

    [Fact]
    public void EmptySeedsAndInvalidActorInputsAreExplicit()
    {
        Assert.Throws<ArgumentNullException>(() => new ArrayCreationRootPlanner(null!));
        var planner = Assert.IsAssignableFrom<IArrayCreationRootPlanner>(new ArrayCreationRootPlanner(new Definitions()));
        Assert.Same(RuntimeIntrinsicTypeRootPlan.Empty, planner.Plan([]));
        Assert.Throws<ArgumentException>(() => planner.Plan(default));
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        internal Dictionary<CliTypeIdentity, TypeDefinitionModel> Items { get; } = [];
        internal List<CliTypeIdentity> Requests { get; } = [];

        internal CliTypeIdentity Add(string name, bool isValue = false, int arity = 0,
            bool byrefLike = false, bool isInterface = false, bool isAbstract = false)
        {
            var definition = new TypeDefinitionModel(
                new(new AssemblyIdentity("array-roots"), 0x02000001 + Items.Count),
                "Fixtures", name, isValue, [], [])
            {
                GenericArity = arity,
                IsByRefLike = byrefLike,
                IsInterface = isInterface,
                IsAbstract = isAbstract,
            };
            var type = CliTypeIdentity.FromDefinition(definition);
            Items.Add(type, definition);
            return type;
        }

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            Requests.Add(identity);
            return Items[identity];
        }
    }
}
