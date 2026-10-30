using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MethodEmissionContextFactoryTests
{
    [Fact]
    public void AllocatesStableNonOverlappingMethodState()
    {
        var body = CreateBody();
        var group = new StructuredExceptionGroupId(0);
        var roots = new MethodRootMap(
            EntryKey,
            [],
            []);
        var values = new ValueFrameLayout(
            8,
            ImmutableDictionary<int, int>.Empty.Add(0, 0),
            [],
            [],
            []);

        var result = MethodEmissionContextFactory.Create(
            Header(body),
            roots,
            2,
            1,
            [group],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var context = result.Context;
        Assert.Equal(2, context.LocalBase);
        Assert.Equal(4, context.StackBase);
        Assert.Equal(19, context.ObjectTemporary);
        Assert.Equal(20, context.RootFrame);
        Assert.Equal(21, context.ExceptionTemporary);
        Assert.Equal(22, context.ExceptionFrameLocals[group]);
        Assert.Equal(23, context.ExceptionContinuationLocals[group]);
        Assert.Equal(24, context.ValueFrame);
        Assert.Equal(25, context.FilterRootFrame);
        Assert.Equal(26, context.InteropDescriptor);
        Assert.Equal(27, context.InteropResult);
        Assert.Equal(28, context.InteropHandle);
        Assert.Equal(29, context.NumericTemporaryI4);
        Assert.Equal(30, context.NumericTemporaryI8);
        Assert.Equal(31, context.DispatcherProgramCounter);
        Assert.Equal(32, context.NumericTemporaryI4Second);
        Assert.Equal(33, context.InlineCatchFrame);
        // Includes the two address scratch locals for inline known-size copies.
        Assert.Equal(34, result.LocalTypes.Length);
        Assert.Equal(0, context.ExceptionRootSlots[group]);
        Assert.Equal(1, context.RootSlotCount);
    }

    [Fact]
    public void ExceptionRootsFollowOrdinarySafepointRootsWithoutAliasing()
    {
        var roots = new MethodRootMap(EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(new(RootSourceKind.Local, 0), 0), []);
        var first = new StructuredExceptionGroupId(3);
        var second = new StructuredExceptionGroupId(19);
        var context = MethodEmissionContextFactory.Create(Header(CreateBody()), roots, 0, 0,
            [first, second], new(0, [], [], [], []), FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller")).Context;
        Assert.Equal(1, context.ExceptionRootSlots[first]);
        Assert.Equal(2, context.ExceptionRootSlots[second]);
        Assert.Equal(3, context.RootSlotCount);
        Assert.Null(context.ActiveCatchRootSlot);
    }

    [Fact]
    public void AllocatesLocalsOnlyForAnchorsNotSharedThroughTheFilterEnvironment()
    {
        var backingType = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Backing",
            isValueType: true);
        var roots = new MethodRootMap(EntryKey, [], [])
        {
            AnchorPlan = new(
            [
                new(new(StackStorageLocationKind.EvaluationStack, 0), backingType),
                new(new(StackStorageLocationKind.Local, 0), backingType),
            ],
            []),
        };
        var filters = FilterEnvironmentLayout.Empty with
        {
            AnchorOffsets = ImmutableDictionary<int, int>.Empty.Add(1, 8),
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            roots,
            2,
            1,
            [],
            new(0, [], [], [], []),
            filters,
            new ManagedMethodIdentity("Tests.Caller"));

        var anchor = Assert.Single(result.Context.AnchorLocals);
        Assert.Equal(0, anchor.Key);
        Assert.Equal(2 + result.LocalTypes.Length - 1, anchor.Value);
        Assert.Equal(CliValueKind.ManagedAddress, result.LocalTypes[^1]);
    }

    [Fact]
    public void AllocatesTwoScalarLocalsForEachPromotedAggregateProducer()
    {
        var element = CliTypeIdentity.Primitive("u8", CliValueKind.I8);
        var plan = new TransientAggregatePromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty
                .Add(11, element)
                .Add(29, element),
            [],
            [],
            []);
        var values = new ValueFrameLayout(0, [], [], [], [])
        {
            TransientAggregatePromotions = plan,
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            new MethodRootMap(EntryKey, [], []),
            0,
            0,
            [],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var first = result.Context.TransientAggregatePromotions.ProducerLocals[11];
        var second = result.Context.TransientAggregatePromotions.ProducerLocals[29];
        Assert.Equal(first.First + 1, first.Second);
        Assert.Equal(first.Second + 1, second.First);
        Assert.Equal(second.First + 1, second.Second);
        Assert.All(
            result.LocalTypes[^4..],
            type => Assert.Equal(CliValueKind.I8, type));
    }

    [Fact]
    public void AllocatesOneAddressShadowLocalForEachPromotedScalar()
    {
        var type = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var plan = new AddressTakenScalarPromotionPlan(
            ImmutableDictionary<int, CliTypeIdentity>.Empty
                .Add(0, type)
                .Add(1, type),
            []);
        var values = new ValueFrameLayout(8, [], [], [], [])
        {
            AddressTakenScalarPromotions = plan,
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            new MethodRootMap(EntryKey, [], []),
            0,
            0,
            [],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var shadows = result.Context.AddressTakenScalarPromotions.ShadowLocals;
        Assert.Equal(shadows[0] + 1, shadows[1]);
        Assert.All(
            result.LocalTypes[^2..],
            local => Assert.Equal(CliValueKind.ManagedAddress, local));
        Assert.Same(plan, result.Context.AddressTakenScalarPromotions.Plan);
    }

    [Fact]
    public void AllocatesDeterministicI4LanesForAddressTakenAggregates()
    {
        var type = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Buffer",
            isValueType: true);
        var plan = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty
                .Add(9, AddressTakenAggregateLocalPlan.CreateI4Storage(type, 12))
                .Add(3, AddressTakenAggregateLocalPlan.CreateI4Storage(type, 8)),
            [],
            []);
        var values = new ValueFrameLayout(20, [], [], [], [])
        {
            AddressTakenAggregatePromotions = plan,
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            new MethodRootMap(EntryKey, [], []),
            0,
            0,
            [],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var storage = result.Context.AddressTakenAggregatePromotions.StorageLocals;
        Assert.Equal(storage[3][^1] + 1, storage[9][0]);
        Assert.Equal(
            [storage[3][0], storage[3][0] + 1],
            storage[3].ToArray());
        Assert.Equal(
            [storage[9][0], storage[9][0] + 1, storage[9][0] + 2],
            storage[9].ToArray());
        Assert.All(
            result.LocalTypes[^5..],
            local => Assert.Equal(CliValueKind.I4, local));
        Assert.Same(plan, result.Context.AddressTakenAggregatePromotions.Plan);
    }

    [Fact]
    public void AllocatesEachAggregateLaneWithItsOwnKind()
    {
        var storage = new ManagedAggregateStorageId(ManagedAggregateStorageKind.Local, 0);
        ImmutableArray<ManagedAggregateSsaLane> lanes = [new(storage, 0), new(storage, 8), new(storage, 12)];
        var plan = ManagedAggregateSsaPlan.Empty with
        {
            Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(storage,
                new(storage, CliTypeIdentity.FromStackKind(CliValueKind.ValueType), 16, true)
                {
                    LaneShapes = [new(0, 8, CliValueKind.ManagedReference), new(8, 4, CliValueKind.I4), new(12, 4, CliValueKind.I4)],
                }),
            Lanes = lanes,
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            new MethodRootMap(EntryKey, [], []),
            0,
            0,
            [],
            new ValueFrameLayout(20, [], [], [], []),
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"),
            plan);

        Assert.Equal(
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4],
            lanes.Select(lane => result.LocalTypes[result.Context.ManagedAggregateValues.LaneLocals[lane]]));
    }

    [Fact]
    public void AllocatesAggregateStorageWithEachPlannedStackKind()
    {
        var type = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Buffer",
            isValueType: true);
        var plan = new AddressTakenAggregatePromotionPlan(
            ImmutableDictionary<int, AddressTakenAggregateLocalPlan>.Empty.Add(
                3,
                new(
                    type,
                    12,
                    [
                        new(0, 4, CliValueKind.I4),
                        new(4, 8, CliValueKind.I8),
                    ])),
            [],
            []);
        var values = new ValueFrameLayout(20, [], [], [], [])
        {
            AddressTakenAggregatePromotions = plan,
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            new MethodRootMap(EntryKey, [], []),
            0,
            0,
            [],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var storage = result.Context.AddressTakenAggregatePromotions.StorageLocals[3];
        Assert.Equal(storage[0] + 1, storage[1]);
        Assert.Equal(
            [CliValueKind.I4, CliValueKind.I8],
            result.LocalTypes[^2..].ToArray());
    }

    [Fact]
    public void AllocatesManagedByRefAggregateStorageWithEachPlannedStackKind()
    {
        var type = CliTypeIdentity.Named(
            Assembly,
            "Tests",
            "Buffer",
            isValueType: true);
        var plan = new ManagedByRefAggregatePromotionPlan(
            ImmutableDictionary<int, ManagedByRefAggregateParameterPlan>.Empty.Add(
                0,
                new(
                    type,
                    12,
                    [
                        new(0, 4, CliValueKind.I4),
                        new(4, 8, CliValueKind.I8),
                    ])),
            []);
        var values = new ValueFrameLayout(0, [], [], [], [])
        {
            ManagedByRefAggregatePromotions = plan,
        };

        var result = MethodEmissionContextFactory.Create(
            Header(CreateBody()),
            new MethodRootMap(EntryKey, [], []),
            1,
            0,
            [],
            values,
            FilterEnvironmentLayout.Empty,
            new ManagedMethodIdentity("Tests.Caller"));

        var storage = result.Context.ManagedByRefAggregatePromotions.StorageLocals[0];
        Assert.Equal(storage[0] + 1, storage[1]);
        Assert.Equal(
            [CliValueKind.I4, CliValueKind.I8],
            result.LocalTypes[^2..].ToArray());
        Assert.Same(plan, result.Context.ManagedByRefAggregatePromotions.Plan);
    }

    private static CilMethodBody CreateBody()
    {
        var method = new MethodDefinitionModel(
            EntryKey,
            TypeKey,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        return new CilMethodBody(
            method,
            3,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            [])
        {
            LocalSignatureTypes =
            [
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            ],
        };
    }
}
