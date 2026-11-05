using System.Collections.Frozen;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A unit keeps a method's exception groups, each clause named by its body exception region;
// an outline stands in for the structured method from them, and bodies whose groups do not
// fit get none.
public sealed class UnitExceptionGroupsTests
{
    private static readonly AssemblyIdentity Tests = new("UnitExceptionGroupsTests");
    private static readonly EntityKey ExceptionKey = new(Tests, 0x02000002);
    private static readonly CliTypeIdentity ExceptionType = CliTypeIdentity.Named(Tests, "System", "Exception", isValueType: false);

    private static readonly MethodDefinitionModel Run = new(new EntityKey(Tests, 0x06000001), new EntityKey(Tests, 0x02000001),
        "Run", true, MethodSignatureModel.Create(CliValueKind.Void), 0);

    // try { try { } catch (Exception) { } } finally { }
    private static readonly ImmutableArray<CilExceptionRegion> Regions =
    [
        new(CilExceptionRegionKind.Finally, 0, 6, 6, 2, null, null),
        new(CilExceptionRegionKind.Catch, 0, 2, 2, 2, ExceptionKey, null) { CatchTypeIdentity = ExceptionType },
    ];

    private static StructuredExceptionClause Catch => new(CilExceptionRegionKind.Catch, 2, 2, ExceptionKey, null,
        StructuredSequence.Empty, null) { CatchTypeIdentity = ExceptionType, HandlerBlock = new(2) };

    private static StructuredExceptionGroup Group(int id, int? parent, int tryOffset, int tryLength, StructuredExceptionClause clause) =>
        new(new(id), parent is { } value ? new(value) : null, tryOffset, tryLength, [], [clause], [], null, null);

    private static StructuredMethod Structured(params StructuredExceptionGroup[] groups) => new(
        new StructuredMethodHeader(Run, null, 1, [], [], []),
        new(0),
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty,
        StructuredSequence.Empty,
        [.. groups.Select(group => group.Id)],
        groups.ToImmutableDictionary(group => group.Id),
        ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

    private static StructuredMethod Nested(StructuredExceptionClause inner, int tryOffset = 0, int tryLength = 2) => Structured(
        Group(0, null, 0, 6, new(CilExceptionRegionKind.Finally, 6, 2, null, null, StructuredSequence.Empty, null)
        {
            HandlerBlock = new(6),
        }),
        Group(1, 0, tryOffset, tryLength, inner));

    [Fact]
    public void EachGroupNamesItsClausesRegionsInTheOrderPlanningReadsThem()
    {
        var groups = UnitExceptionGroups.Of(Nested(Catch), Regions, new Order(1, 0));

        Assert.NotNull(groups);
        Assert.Equal([(1, 0, 0, 2, 1), (0, -1, 0, 6, 0)],
            groups.Value.Select(group => (group.Id, group.Parent, group.TryOffset, group.TryLength, Assert.Single(group.Regions))));
        Assert.Empty(UnitExceptionGroups.Of(Structured(), Regions, new Order())!);
    }

    // Each part of a clause names its region: one that differs in any names none.
    public static TheoryData<string> Differences => new()
    {
        "kind", "try offset", "try length", "handler offset", "handler length", "catch type", "filter offset", "filter offset value",
        "catch type identity",
    };

    [Theory]
    [MemberData(nameof(Differences))]
    public void AClauseThatIsNotOneOfTheBodysRegionsHasNoGroups(string difference)
    {
        var method = difference switch
        {
            "kind" => Nested(Catch with { Kind = CilExceptionRegionKind.Fault }),
            "try offset" => Nested(Catch, tryOffset: 1),
            "try length" => Nested(Catch, tryLength: 3),
            "handler offset" => Nested(Catch with { HandlerOffset = 3 }),
            "handler length" => Nested(Catch with { HandlerLength = 1 }),
            "catch type" => Nested(Catch with { CatchType = new EntityKey(Tests, 0x02000009) }),
            "filter offset" => Nested(Catch with { FilterOffset = 5 }),
            "filter offset value" => Nested(Catch with { FilterOffset = 5 }),
            _ => Nested(Catch with { CatchTypeIdentity = CliTypeIdentity.Named(Tests, "System", "Other", isValueType: false) }),
        };

        // Each region the clause is compared with, the catch region with a filter offset of its own.
        var regions = difference == "filter offset value" ? Regions.SetItem(1, Regions[1] with { FilterOffset = 3 }) : Regions;

        Assert.Null(UnitExceptionGroups.Of(method, regions, new Order(1, 0)));
    }

    // A filter's groups are not kept, nor are those of a method planning reads only in part.
    [Fact]
    public void FiltersAndGroupsPlanningDoesNotReadHaveNoGroups()
    {
        Assert.Null(UnitExceptionGroups.Of(Nested(Catch with { Kind = CilExceptionRegionKind.Filter }), Regions, new Order(1, 0)));
        Assert.Null(UnitExceptionGroups.Of(Nested(Catch with { FilterBody = StructuredSequence.Empty }), Regions, new Order(1, 0)));
        Assert.Null(UnitExceptionGroups.Of(Nested(Catch), Regions, new Order(1)));
    }

    [Fact]
    public void AnOutlineStandsInWithTheGroupsClausesFromTheBody()
    {
        var body = CatchBody();

        var outline = UnitExceptionGroups.Outline(body, [new(0, -1, 0, 2, [0]), new(1, 0, 0, 2, [0])],
            new(body, BranchPrefixDuplicationPolicy.Disabled, DispatcherEliminationPolicy.Disabled));

        Assert.NotNull(outline);
        Assert.NotNull(outline.Outline);
        Assert.Equal([new(0), new(1)], outline.TopLevelExceptionGroups);
        Assert.Null(outline.ExceptionGroups[new(0)].Parent);
        Assert.Equal(new StructuredExceptionGroupId(0), outline.ExceptionGroups[new(1)].Parent);
        var clause = Assert.Single(outline.ExceptionGroups[new(1)].Clauses);
        Assert.Equal((CilExceptionRegionKind.Catch, 2, 2, ExceptionKey, ExceptionType),
            (clause.Kind, clause.HandlerOffset, clause.HandlerLength, clause.CatchType!.Value, clause.CatchTypeIdentity));
    }

    // Groups that do not fit the body, as a damaged unit might keep them, give no outline.
    [Theory]
    [InlineData("duplicate id")]
    [InlineData("negative region")]
    [InlineData("region past the end")]
    [InlineData("filter region")]
    public void GroupsThatDoNotFitTheBodyGiveNoOutline(string damage)
    {
        var body = damage == "filter region" ? FilterBody() : CatchBody();
        ImmutableArray<UnitExceptionGroup> groups = damage switch
        {
            "duplicate id" => [new(0, -1, 0, 2, [0]), new(0, -1, 0, 2, [0])],
            "negative region" => [new(0, -1, 0, 2, [-1])],
            "region past the end" => [new(0, -1, 0, 2, [1])],
            _ => [new(0, -1, 1, 2, [0])],
        };

        Assert.Null(UnitExceptionGroups.Outline(body, groups,
            new(body, BranchPrefixDuplicationPolicy.Disabled, DispatcherEliminationPolicy.Disabled)));
    }

    // Groups are alike group by group and clause by clause; where they sit may move.
    [Theory]
    [InlineData("moved", true)]
    [InlineData("count", false)]
    [InlineData("id", false)]
    [InlineData("parent", false)]
    [InlineData("regions", false)]
    public void GroupsAreAlikeByIdParentAndRegions(string difference, bool same)
    {
        ImmutableArray<UnitExceptionGroup> before = [new(0, -1, 0, 6, [0]), new(1, 0, 0, 2, [1])];
        ImmutableArray<UnitExceptionGroup> now = difference switch
        {
            "moved" => [new(0, -1, 4, 6, [0]), new(1, 0, 4, 2, [1])],
            "count" => [new(0, -1, 0, 6, [0])],
            "id" => [new(0, -1, 0, 6, [0]), new(2, 0, 0, 2, [1])],
            "parent" => [new(0, -1, 0, 6, [0]), new(1, -1, 0, 2, [1])],
            _ => [new(0, -1, 0, 6, [0]), new(1, 0, 0, 2, [0])],
        };

        Assert.Equal(same, UnitExceptionGroups.Same(before, now));
    }

    // A method whose root map this compile did not plan has no unit to reuse.
    [Fact]
    public void AMethodWithoutARootMapIsNotOutlined()
    {
        using var state = new CompiledUnitState(null);
        var outlines = new CompiledUnitOutlines(state, FrozenDictionary<string, MethodRootMap>.Empty,
            BranchPrefixDuplicationPolicy.Disabled, DispatcherEliminationPolicy.Disabled);

        Assert.Null(outlines.Outline(CatchBody()));
        Assert.Equal(0, state.Outlined);
    }

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    // try { } catch (Exception) { }
    private static ManagedMethodBody CatchBody() => Managed(new CilMethodBody(Run, 1, [],
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.Return),
        ])
    {
        ExceptionRegions = [new(CilExceptionRegionKind.Catch, 0, 2, 2, 2, ExceptionKey, null) { CatchTypeIdentity = ExceptionType }],
    });

    // try { } catch when (true) { }
    private static ManagedMethodBody FilterBody() => Managed(new CilMethodBody(Run, 1, [],
        [
            I(0, CilOperation.Nop),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(5, CilOperation.EndFilter),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(8, CilOperation.Return),
        ])
    {
        ExceptionRegions = [new(CilExceptionRegionKind.Filter, 1, 2, 6, 2, null, 3)],
    });

    private static ManagedMethodBody Managed(CilMethodBody body)
    {
        var method = new MethodInstanceModel(Run, CliTypeIdentity.Named(Tests, "Tests", "Program", isValueType: false), [],
            Run.Signature);
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body with { MethodInstance = method });
        return new(method, new ValidatedControlFlowGraph(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, [])));
    }

    private sealed class Order(params int[] ids) : IExceptionGroupEnumerator
    {
        public IEnumerable<StructuredExceptionGroupId> Enumerate(StructuredMethod method) => ids.Select(id => new StructuredExceptionGroupId(id));
    }
}
