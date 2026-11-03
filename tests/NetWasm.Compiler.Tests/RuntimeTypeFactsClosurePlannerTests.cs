using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeTypeFactsClosurePlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("Facts");
    private static readonly CliTypeIdentity Box = Named("Box");
    private static readonly CliTypeIdentity Argument = Named("Argument");
    private static readonly CliTypeIdentity Parent = Named("Parent");

    [Fact]
    public void InvalidDependenciesAndUninitializedSeedsAreRejected()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeFactsClosurePlanner(null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeFactsClosurePlanner(fixture, null!));
        Assert.Throws<ArgumentException>(() => Plan(fixture.Create(), default, false));
        Assert.Empty(Plan(fixture.Create(), [], true));
        Assert.Empty(fixture.BaseRequests);
    }

    [Fact]
    public void OrdinaryFactsPreserveBaseRootsWithoutRetainingGenericArguments()
    {
        var fixture = new Fixture();
        var closed = CliTypeIdentity.GenericInstantiation(Box, [Argument]);
        fixture.Bases[Argument] = Parent;
        var roots = Plan(fixture.Create(), [closed], false);
        Assert.Equal([Parent], roots.ToArray());
        Assert.Contains(Argument, fixture.BaseRequests);
        Assert.DoesNotContain(Argument, roots);
        Assert.DoesNotContain(Box, roots);
    }

    [Fact]
    public void GenericDemandRetainsNestedArgumentsAndArrayElements()
    {
        var fixture = new Fixture();
        var number = CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt);
        var inner = CliTypeIdentity.GenericInstantiation(Box, [number]);
        var array = CliTypeIdentity.SzArray(inner);
        var outer = CliTypeIdentity.GenericInstantiation(Box, [array]);
        var roots = Plan(fixture.Create(), [outer, outer], true);
        Assert.Equal(4, roots.Length);
        Assert.True(roots.ToHashSet().SetEquals([outer, array, inner, number]));
        Assert.DoesNotContain(Box, roots);
    }

    [Fact]
    public void ElementModifiersRetainTheirOwnAndElementIdentities()
    {
        var fixture = new Fixture();
        var pointer = CliTypeIdentity.UnmanagedPointer(Argument);
        var reference = CliTypeIdentity.ManagedByReference(pointer);
        var roots = Plan(fixture.Create(), [reference], true);
        Assert.True(roots.ToHashSet().SetEquals([reference, pointer, Argument]));
    }

    [Fact]
    public void OpenDefinitionsAndParametersDoNotExpandTheirHierarchy()
    {
        var fixture = new Fixture();
        fixture.Open.Add(Box);
        var parameter = CliTypeIdentity.GenericParameter(false, 0);
        var constructed = CliTypeIdentity.GenericInstantiation(Box, [parameter]);
        Assert.Equal([Box], Plan(fixture.Create(), [Box, parameter, constructed], true).ToArray());
        Assert.Empty(fixture.BaseRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenBaseTypesAreNotRetained(bool parameter)
    {
        var fixture = new Fixture();
        fixture.Open.Add(Box);
        fixture.Bases[Argument] = parameter ? CliTypeIdentity.GenericParameter(false, 0) : Box;
        Assert.Empty(Plan(fixture.Create(), [Argument], false));
        Assert.Equal([Argument], fixture.BaseRequests);
    }

    [Fact]
    public void DuplicateSeedsAndHierarchyCyclesTerminate()
    {
        var fixture = new Fixture();
        fixture.Bases[Argument] = Parent;
        fixture.Bases[Parent] = Argument;
        var roots = Plan(fixture.Create(), [Argument, Argument], false);
        Assert.True(roots.ToHashSet().SetEquals([Argument, Parent]));
        Assert.Equal(2, fixture.BaseRequests.Count);
    }

    private static ImmutableArray<CliTypeIdentity> Plan<TPlanner>(TPlanner planner,
        ImmutableArray<CliTypeIdentity> seeds, bool includeGenericArguments)
        where TPlanner : IRuntimeTypeFactsClosurePlanner => planner.Plan(seeds, includeGenericArguments);

    private static CliTypeIdentity Named(string name) => CliTypeIdentity.Named(Assembly, "Fixture", name, false);

    private sealed class Fixture : IBaseTypeResolver, ITypeDefinitionResolver
    {
        internal Dictionary<CliTypeIdentity, CliTypeIdentity> Bases { get; } = [];
        internal HashSet<CliTypeIdentity> Open { get; } = [];
        internal List<CliTypeIdentity> BaseRequests { get; } = [];
        internal RuntimeTypeFactsClosurePlanner Create() => new RuntimeTypeFactsClosurePlanner(this, this);
        public CliTypeIdentity? Resolve(CliTypeIdentity type)
        {
            BaseRequests.Add(type);
            return Bases.GetValueOrDefault(type);
        }
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity type) =>
            new(new(Assembly, 1), "Fixture", "Type", false, [], []) { GenericArity = Open.Contains(type) ? 1 : 0 };
    }
}
