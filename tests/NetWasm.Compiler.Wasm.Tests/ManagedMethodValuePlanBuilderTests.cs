using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedMethodValuePlanBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportsOwnedLocalStorageAndWideIntegerLanePairs(bool wide)
    {
        var storage = Storage with { Id = new(ManagedAggregateStorageKind.Local, 0) };
        var first = new ManagedAggregateSsaLane(storage.Id, 0);
        var second = new ManagedAggregateSsaLane(storage.Id, 4);
        var kind = wide ? CliValueKind.I8 : CliValueKind.I4;
        var plan = new ManagedAggregateSsaPlan(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(storage.Id, storage),
            [first, second], ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>.Empty
                .Add(first, new(0)).Add(second, new(1)), [],
            ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty.Add(1, new(
                new(1, new(0), new(storage.Id, 0, CliTypeIdentity.FromStackKind(kind)), wide ? 8 : 4, kind, true),
                [], wide ? [new(first, new(2)), new(second, new(3))] : [new(first, new(2))])),
            [], [], [], []);
        var original = RootMap();

        var outcome = Build(original, plan, [], true);

        Assert.Same(plan, outcome.Result.Aggregates);
        Assert.NotSame(original, outcome.Result.EmissionRootMap);
        Assert.Equal(1, outcome.Projector.CallCount);
    }

    [Fact]
    public void SupportsReferenceLanesAndClearsOfEveryLane()
    {
        var storage = Storage with
        {
            Id = new(ManagedAggregateStorageKind.Local, 0),
            LaneShapes = [new(0, 4, CliValueKind.ManagedReference), new(4, 4, CliValueKind.I4)],
        };
        var first = new ManagedAggregateSsaLane(storage.Id, 0);
        var second = new ManagedAggregateSsaLane(storage.Id, 4);
        var recipe = new ManagedAggregateAddressRecipe(storage.Id, 0, storage.Type);
        var plan = new ManagedAggregateSsaPlan(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(storage.Id, storage),
            [first, second], ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>.Empty
                .Add(first, new(0)).Add(second, new(1)), [],
            ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty
                .Add(1, new(new(1, new(0), recipe, 4, CliValueKind.ValueType, true) { LaneOffsets = [0, 4] },
                    [], [new(first, new(2)), new(second, new(3))]))
                .Add(2, new(new(2, new(0), recipe, 4, CliValueKind.ManagedReference, false) { LaneOffsets = [0] },
                    [new(first, new(2))], [])),
            [], [], [], []);

        var outcome = Build(RootMap(), plan, [], true);

        Assert.Same(plan, outcome.Result.Aggregates);
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("unknown-storage")]
    [InlineData("empty-size")]
    [InlineData("missing-materialization")]
    [InlineData("missing-lane")]
    [InlineData("unaligned-access")]
    [InlineData("wrong-size")]
    [InlineData("wrong-kind")]
    [InlineData("missing-value")]
    [InlineData("missing-lane-value")]
    [InlineData("reading-clear")]
    public void UnsupportedEmissionPlansDoNotPublishProjectedRoots(string invalid)
    {
        var rootMap = RootMap();
        var plan = Plan();
        var access = plan.Accesses[1];
        var unknownStorage = new ManagedAggregateStorageId((ManagedAggregateStorageKind)99, 0);
        plan = invalid switch
        {
            "unknown-storage" => plan with
            {
                Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty
                .Add(unknownStorage, Storage with { Id = unknownStorage })
            },
            "unowned" => plan with { Storages = plan.Storages.SetItem(Storage.Id, Storage with { Owned = false }) },
            "empty-size" => plan with { Storages = plan.Storages.SetItem(Storage.Id, Storage with { Size = 0 }) },
            "missing-materialization" => plan with { Materializations = [] },
            "missing-lane" => plan with { Lanes = [plan.Lanes[0]] },
            "unaligned-access" => plan with
            {
                Accesses = plan.Accesses.SetItem(1,
                access with { Access = access.Access with { Address = access.Access.Address with { ByteOffset = 2 } } })
            },
            "wrong-size" => plan with { Accesses = plan.Accesses.SetItem(1, access with { Access = access.Access with { Size = 2 } }) },
            "wrong-kind" => plan with { Accesses = plan.Accesses.SetItem(1, access with { Access = access.Access with { StackKind = CliValueKind.F4 } }) },
            "missing-value" => plan with { Accesses = plan.Accesses.SetItem(1, access with { Writes = [] }) },
            "missing-lane-value" => plan with
            {
                Accesses = plan.Accesses.SetItem(1, access with { Access = access.Access with { LaneOffsets = [0] }, Writes = [] }),
            },
            "reading-clear" => plan with
            {
                Accesses = plan.Accesses.SetItem(1, access with
                {
                    Access = access.Access with { StackKind = CliValueKind.ValueType, LaneOffsets = [0] },
                    Reads = access.Writes,
                    Writes = [],
                }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };

        var outcome = Build(rootMap, plan, [], true);

        Assert.Same(rootMap, outcome.Result.EmissionRootMap);
        Assert.Same(ManagedAggregateSsaPlan.Empty, outcome.Result.Aggregates);
        Assert.Equal(0, outcome.Projector.CallCount);
    }

    private static readonly EntityKey MethodKey = new(new("value-plan"), 1);
    private static readonly ManagedAggregateStorage Storage = new(
        new(ManagedAggregateStorageKind.Argument, 0),
        CliTypeIdentity.FromStackKind(CliValueKind.ValueType),
        8,
        true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnsAggregateAndProjectedRootMapAtomically(bool references)
    {
        var original = RootMap();
        var projected = RootMap(2);
        var analyzer = new RecordingAnalyzer();
        var planner = new RecordingPlanner(Plan());
        var projector = new RecordingProjector(true, projected);
        var builder = new ManagedMethodValuePlanBuilder(analyzer, planner, projector, EmptyTransientAggregatePromotionPlanner.Instance);

        var result = builder.Build(Request(original, enabled: true) with { References = references });

        Assert.Same(projected, result.EmissionRootMap);
        Assert.Same(planner.Output, result.Aggregates);
        Assert.True(analyzer.Request!.Enabled);
        Assert.Equal(references, analyzer.Request.References);
        Assert.Equal(references, planner.Request!.References);
        Assert.Equal(1, projector.CallCount);
    }

    [Fact]
    public void FallsBackAtomicallyForEmptyReservedOrUnprojectablePlans()
    {
        var rootMap = RootMap();

        var empty = Build(rootMap, ManagedAggregateSsaPlan.Empty, [], true);
        Assert.Same(rootMap, empty.Result.EmissionRootMap);
        Assert.Same(ManagedAggregateSsaPlan.Empty, empty.Result.Aggregates);
        Assert.Equal(0, empty.Projector.CallCount);

        var reserved = Build(rootMap, Plan(), [Storage.Id], true);
        Assert.Same(rootMap, reserved.Result.EmissionRootMap);
        Assert.Same(ManagedAggregateSsaPlan.Empty, reserved.Result.Aggregates);
        Assert.Equal(0, reserved.Projector.CallCount);

        var failed = Build(rootMap, Plan(), [], false);
        Assert.Same(rootMap, failed.Result.EmissionRootMap);
        Assert.Same(ManagedAggregateSsaPlan.Empty, failed.Result.Aggregates);
        Assert.Equal(1, failed.Projector.CallCount);

        var partialLane = Plan() with
        {
            Storages = ImmutableDictionary<
                ManagedAggregateStorageId,
                ManagedAggregateStorage>.Empty.Add(
                    Storage.Id,
                    Storage with { Size = 10 }),
        };
        var unsupported = Build(rootMap, partialLane, [], true);
        Assert.Same(rootMap, unsupported.Result.EmissionRootMap);
        Assert.Same(ManagedAggregateSsaPlan.Empty, unsupported.Result.Aggregates);
        Assert.Equal(0, unsupported.Projector.CallCount);
    }

    [Fact]
    public void DisabledPlanningDoesNotInvokeAnyPlanningActor()
    {
        var rootMap = RootMap();
        var analyzer = new RecordingAnalyzer();
        var planner = new RecordingPlanner(Plan());
        var projector = new RecordingProjector(true, RootMap(2));
        var builder = new ManagedMethodValuePlanBuilder(analyzer, planner, projector, EmptyTransientAggregatePromotionPlanner.Instance);

        var result = builder.Build(Request(rootMap, enabled: false));

        Assert.Same(rootMap, result.EmissionRootMap);
        Assert.Same(ManagedAggregateSsaPlan.Empty, result.Aggregates);
        Assert.Null(analyzer.Request);
        Assert.Equal(0, planner.CallCount);
        Assert.Equal(0, projector.CallCount);
    }

    [Fact]
    public void LeavesTransientLocalsOutOfTheAnalysis()
    {
        var analyzer = new RecordingAnalyzer();
        var builder = new ManagedMethodValuePlanBuilder(analyzer, new RecordingPlanner(Plan()),
            new RecordingProjector(true, RootMap()), new FixedTransientPlanner([3]));

        builder.Build(Request(RootMap(), enabled: true));

        Assert.Equal([3], analyzer.Request!.ExcludedLocals);
    }

    [Fact]
    public void RejectsNullDependenciesAndRequestMembers()
    {
        var analyzer = new RecordingAnalyzer();
        var planner = new RecordingPlanner(ManagedAggregateSsaPlan.Empty);
        var projector = new RecordingProjector(true, RootMap());
        var builder = new ManagedMethodValuePlanBuilder(analyzer, planner, projector, EmptyTransientAggregatePromotionPlanner.Instance);
        var request = Request(RootMap(), enabled: false);

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodValuePlanBuilder(null!, planner, projector, EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodValuePlanBuilder(analyzer, null!, projector, EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodValuePlanBuilder(analyzer, planner, null!, EmptyTransientAggregatePromotionPlanner.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new ManagedMethodValuePlanBuilder(analyzer, planner, projector, null!));
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<ArgumentNullException>(() =>
            builder.Build(request with { Method = null! }));
        Assert.Throws<ArgumentNullException>(() =>
            builder.Build(request with { RootMap = null! }));
        Assert.Throws<ArgumentNullException>(() =>
            builder.Build(request with { ByRefEffects = null! }));
        Assert.Throws<ArgumentNullException>(() =>
            builder.Build(request with { ReservedStorages = null! }));
    }

    private static (
        ManagedMethodValuePlan Result,
        RecordingProjector Projector) Build(
            MethodRootMap rootMap,
            ManagedAggregateSsaPlan plan,
            ImmutableHashSet<ManagedAggregateStorageId> reserved,
            bool project)
    {
        var projector = new RecordingProjector(project, RootMap(3));
        var builder = new ManagedMethodValuePlanBuilder(
            new RecordingAnalyzer(),
            new RecordingPlanner(plan),
            projector,
            EmptyTransientAggregatePromotionPlanner.Instance);
        return (builder.Build(Request(rootMap, reserved, enabled: true)), projector);
    }

    private static ManagedMethodValuePlanRequest Request(
        MethodRootMap rootMap,
        ImmutableHashSet<ManagedAggregateStorageId>? reserved = null,
        bool enabled = false) => new(
        Method(),
        rootMap,
        ManagedByRefEffects.Empty,
        reserved ?? [],
        enabled);

    private static StructuredMethod Method()
    {
        var definition = new MethodDefinitionModel(
            MethodKey,
            new(new("value-plan"), 2),
            "Method",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        return new(
            new(definition, null, 0, [], [], []),
            new(0),
            [],
            StructuredSequence.Empty,
            [],
            [],
            []);
    }

    private static MethodRootMap RootMap(int offset = 1) => new(
        MethodKey,
        [],
        ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
            offset,
            new(offset, [], [])));

    private static ManagedAggregateSsaPlan Plan() => new(
        ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty
            .Add(Storage.Id, Storage),
        [new(Storage.Id, 0), new(Storage.Id, sizeof(int))],
        ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>.Empty
            .Add(new(Storage.Id, 0), new(0))
            .Add(new(Storage.Id, sizeof(int)), new(1)),
        [],
        ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty.Add(
            1,
            new(
                new(
                    1,
                    new(0),
                    new(Storage.Id, 0, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                    sizeof(int),
                    CliValueKind.I4,
                    Writes: true),
                [],
                [new(new(Storage.Id, 0), new(2))])),
        [],
        [],
        [new(
            new(2, Storage.Id),
            [new(new(Storage.Id, 0), new(2)),
                new(new(Storage.Id, sizeof(int)), new(1))])],
        []);

    private sealed class FixedTransientPlanner(ImmutableHashSet<int> locals) : ITransientAggregatePromotionPlanner
    {
        public TransientAggregatePromotionPlan Create(StructuredMethod method) =>
            TransientAggregatePromotionPlan.Empty with { PromotedLocalIndices = locals };
    }

    private sealed class RecordingAnalyzer : IManagedAggregateValueAnalyzer
    {
        public ManagedAggregateValueAnalysisRequest? Request { get; private set; }

        public ManagedAggregateValueAnalysis Analyze(
            ManagedAggregateValueAnalysisRequest request)
        {
            Request = request;
            return ManagedAggregateValueAnalysis.Empty;
        }
    }

    private sealed class RecordingPlanner(ManagedAggregateSsaPlan plan) :
        IManagedAggregateSsaPlanner
    {
        public ManagedAggregateSsaPlan Output { get; } = plan;

        public int CallCount { get; private set; }

        public ManagedAggregateSsaPlanningRequest? Request { get; private set; }

        public ManagedAggregateSsaPlan Plan(ManagedAggregateSsaPlanningRequest request)
        {
            CallCount++;
            Request = request;
            return Output;
        }
    }

    private sealed class RecordingProjector(
        bool succeeds,
        MethodRootMap projected) : IManagedAggregateRootMapProjector
    {
        public int CallCount { get; private set; }

        public bool TryProject(
            ManagedAggregateRootMapProjectionRequest request,
            out MethodRootMap rootMap)
        {
            CallCount++;
            rootMap = succeeds ? projected : request.RootMap;
            return succeeds;
        }
    }
}
