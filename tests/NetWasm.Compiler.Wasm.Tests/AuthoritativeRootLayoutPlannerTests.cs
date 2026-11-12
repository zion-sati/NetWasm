using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AuthoritativeRootLayoutPlannerTests
{
    private readonly IAuthoritativeRootLayoutPlanner _planner =
        new[] { new AuthoritativeRootLayoutPlanner() }
            .Cast<IAuthoritativeRootLayoutPlanner>().Single();

    [Fact]
    public void StableVariablesCannotAliasEachOtherOrTemporaryRoots()
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        var local = new RootSource(RootSourceKind.Local, 0);
        var temporary = new RootSource(RootSourceKind.EvaluationStack, 0);
        var otherTemporary = new RootSource(RootSourceKind.EvaluationStack, 1);
        var map = new MethodRootMap(EntryKey,
            new Dictionary<RootSource, int>
            {
                [argument] = 0,
                [local] = 0,
                [temporary] = 0,
                [otherTemporary] = 0,
            }.ToImmutableDictionary(), []);

        var result = _planner.Plan(map, [argument, local]);

        Assert.Equal(3, result.RootMap.SlotCount);
        Assert.Equal(0, result.Slots[argument]);
        Assert.Equal(1, result.Slots[local]);
        Assert.Equal(2, result.RootMap.Slots[temporary]);
        Assert.Equal(2, result.RootMap.Slots[otherTemporary]);
        Assert.Same(map.Safepoints, result.RootMap.Safepoints);
        Assert.Same(map.AnchorPlan, result.RootMap.AnchorPlan);
        Assert.Equal(1, map.SlotCount);
    }

    [Fact]
    public void EmptySelectionPreservesTheOriginalMap()
    {
        var map = new MethodRootMap(EntryKey, [], []);
        var result = _planner.Plan(map, []);
        Assert.Same(map, result.RootMap);
        Assert.Empty(result.Slots);
    }

    [Theory]
    [InlineData(RootSourceKind.EvaluationStack, -1)]
    [InlineData(RootSourceKind.ManagedAddressLocal, -1)]
    [InlineData(RootSourceKind.Local, 0)]
    public void NonVariableOrIndirectSelectionIsRejected(RootSourceKind kind, int offset)
    {
        var source = new RootSource(kind, 0, offset);
        var map = new MethodRootMap(EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(source, 0), []);
        Assert.Throws<ArgumentException>(() => _planner.Plan(map, [source]));
    }

    [Fact]
    public void AByrefArgumentGetsAStableSlot()
    {
        var receiver = new RootSource(RootSourceKind.ManagedAddressArgument, 0);
        var other = new RootSource(RootSourceKind.EvaluationStack, 0);
        var map = new MethodRootMap(EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(other, 0).Add(receiver, 0), []);

        var result = _planner.Plan(map, [receiver]);

        Assert.Equal(0, result.Slots[receiver]);
        Assert.Equal(1, result.RootMap.Slots[other]);
    }

    [Fact]
    public void MissingSelectionIsRejected()
    {
        var map = new MethodRootMap(EntryKey, [], []);
        Assert.Throws<ArgumentException>(() => _planner.Plan(map, [new(RootSourceKind.Local, 0)]));
    }
}
