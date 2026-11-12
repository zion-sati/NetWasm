using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

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
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeFactsClosurePlanner(null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeFactsClosurePlanner(fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new RuntimeTypeFactsClosurePlanner(fixture, fixture, null!));
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

    // DeclaringType can return the enclosing definition of any registered nested type, and that
    // definition's own enclosing one; without the demand they stay out.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaringDemandRetainsEveryEnclosingDefinition(bool declaring)
    {
        var fixture = new Fixture();
        fixture.Nested[Argument] = Parent;
        fixture.Nested[Parent] = Box;
        fixture.Open.Add(Box);

        var roots = Plan(fixture.Create(), [Argument], false, declaring);

        Assert.Equal(declaring, roots.ToHashSet().SetEquals([Parent, Box]));
        Assert.Equal(!declaring, roots.IsEmpty);
    }

    // GetGenericTypeDefinition can return the open definition of any registered instantiation.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitionDemandRetainsEveryInstantiationsDefinition(bool definitions)
    {
        var fixture = new Fixture();
        fixture.Open.Add(Box);
        var closed = CliTypeIdentity.GenericInstantiation(Box, [Argument]);

        var roots = Plan(fixture.Create(), [closed], false, includeGenericDefinitions: definitions);

        Assert.Equal(definitions, roots.Contains(Box));
        Assert.Equal(!definitions, roots.IsEmpty);
    }

    // GetElementType can return the element of any registered array, pointer or by-reference type.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ElementDemandRetainsEveryElement(bool elements)
    {
        var fixture = new Fixture();
        var array = CliTypeIdentity.Array(Argument, 2);
        var pointer = CliTypeIdentity.UnmanagedPointer(Parent);
        var reference = CliTypeIdentity.ManagedByReference(Box);
        var list = CliTypeIdentity.SzArray(array);

        var roots = Plan(fixture.Create(), [list, pointer, reference], false, includeElementTypes: elements);

        Assert.Equal(elements, roots.ToHashSet().SetEquals([array, Argument, Parent, Box]));
        Assert.Equal(!elements, roots.IsEmpty);
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
        ImmutableArray<CliTypeIdentity> seeds, bool includeGenericArguments, bool includeDeclaringTypes = false,
        bool includeGenericDefinitions = false, bool includeElementTypes = false)
        where TPlanner : IRuntimeTypeFactsClosurePlanner =>
        planner.Plan(seeds, includeGenericArguments, includeDeclaringTypes, includeGenericDefinitions, includeElementTypes);

    private static CliTypeIdentity Named(string name) => CliTypeIdentity.Named(Assembly, "Fixture", name, false);

    private sealed class Fixture : IBaseTypeResolver, ITypeDefinitionResolver, ITypeIdentityResolver
    {
        internal Dictionary<CliTypeIdentity, CliTypeIdentity> Bases { get; } = [];
        internal Dictionary<CliTypeIdentity, CliTypeIdentity> Nested { get; } = [];
        internal HashSet<CliTypeIdentity> Open { get; } = [];
        internal List<CliTypeIdentity> BaseRequests { get; } = [];
        private readonly List<CliTypeIdentity> _keys = [];
        internal RuntimeTypeFactsClosurePlanner Create() => new RuntimeTypeFactsClosurePlanner(this, this, this);
        public CliTypeIdentity GetTypeIdentity(EntityKey type) => _keys[type.MetadataToken];
        public CliTypeIdentity? Resolve(CliTypeIdentity type)
        {
            BaseRequests.Add(type);
            return Bases.GetValueOrDefault(type);
        }
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity type) =>
            new(new(Assembly, 1), "Fixture", "Type", false, [], [])
            {
                GenericArity = Open.Contains(type) ? 1 : 0,
                DeclaringType = Nested.TryGetValue(type, out var declaring) ? Key(declaring) : null,
            };

        private EntityKey Key(CliTypeIdentity type)
        {
            _keys.Add(type);
            return new EntityKey(Assembly, _keys.Count - 1);
        }
    }
}
