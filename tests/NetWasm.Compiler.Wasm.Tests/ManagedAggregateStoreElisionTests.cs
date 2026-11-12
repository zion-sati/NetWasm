using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// A call that takes a split aggregate's address stores its lanes first, unless the frame already
// holds a lane's value: stored or reloaded there on every path since, and not written in the lane.
public sealed class ManagedAggregateStoreElisionTests
{
    // A block's held values stopped changing only when its exit holds the same value in every lane.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void HeldValuesSettleOnlyWhenEveryLaneHoldsTheSameValue(int change, bool same)
    {
        var first = new ManagedAggregateSsaLane(new(ManagedAggregateStorageKind.Local, 0), 0);
        var second = new ManagedAggregateSsaLane(new(ManagedAggregateStorageKind.Local, 0), 4);
        var prior = ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>.Empty
            .Add(first, new(1)).Add(second, new(2));
        var exit = change switch
        {
            0 => prior,
            1 => prior.Remove(second),
            2 => prior.Remove(second).Add(new(new(ManagedAggregateStorageKind.Argument, 0), 4), new(2)),
            _ => prior.SetItem(second, new(3)),
        };

        Assert.Equal(same, ManagedAggregateSsaPlanner.HoldsTheSame(prior, exit));
    }

    private static readonly ManagedAggregateStorageId Pair = new(ManagedAggregateStorageKind.Local, 0);

    [Fact]
    public void ACallAfterAReloadStoresNothingAndAWrittenLaneIsStoredAgain()
    {
        var unchanged = Plan(BlockMethods.Of(BlockMethods.Block(0,
            [I(0, CilOperation.Nop), I(1, CilOperation.Nop), I(2, CilOperation.Nop), I(3, CilOperation.Nop)],
            new StructuredTerminalExit(I(4, CilOperation.Return)))), [Write(0, 0), Read(3, 0)], [Call(1), Call(2)]);
        var written = Plan(BlockMethods.Of(BlockMethods.Block(0,
            [I(0, CilOperation.Nop), I(1, CilOperation.Nop), I(2, CilOperation.Nop), I(3, CilOperation.Nop), I(4, CilOperation.Nop)],
            new StructuredTerminalExit(I(5, CilOperation.Return)))), [Write(0, 0), Write(2, 0), Read(4, 0)], [Call(1), Call(3)]);

        Assert.Equal([0, 4], Stored(unchanged, 1));
        Assert.Empty(Stored(unchanged, 2));
        Assert.Equal(2, unchanged.Materializations[1].Reloads.Length);
        Assert.Equal([0], Stored(written, 3));
    }

    // The loop's phi is held at its header when the frame holds each incoming value: the reload
    // before the loop's first call and the one after its last.
    [Fact]
    public void ALoopsCallStoresNothingWhenEveryEdgeIntoItHoldsTheLanes()
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Nop), I(1, CilOperation.Nop)], new StructuredFallthroughExit(new(1))),
            BlockMethods.Block(1, [I(2, CilOperation.Nop), I(3, CilOperation.Nop)], new StructuredConditionalExit(
                new(4, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(2, [I(5, CilOperation.Nop)], new StructuredTerminalExit(I(6, CilOperation.Return))));

        var plan = Plan(method, [Write(0, 0), Read(2, 0)], [Call(1), Call(3)]);

        Assert.Equal([0, 4], Stored(plan, 1));
        Assert.Empty(Stored(plan, 3));
        Assert.Contains(plan.Phis, phi => phi.Block == new StructuredBlockId(1));
    }

    // Where one path into a join stored nothing, the frame may hold anything: whether the paths'
    // values differ (a phi joins them) or are the same value.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AJoinHoldsALaneOnlyWhereEveryPathIntoItDoes(bool valuesDiffer)
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Nop)], new StructuredConditionalExit(
                new(1, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(1, [I(2, CilOperation.Nop)], new StructuredBranchExit(3, new(3))),
            BlockMethods.Block(2, [I(4, CilOperation.Nop)], new StructuredFallthroughExit(new(3))),
            BlockMethods.Block(3, [I(6, CilOperation.Nop), I(7, CilOperation.Nop)], new StructuredTerminalExit(I(8, CilOperation.Return))));

        var plan = Plan(method, [Write(0, 0), Read(7, 0)], [Call(2, reload: valuesDiffer), Call(6)]);

        Assert.Equal(valuesDiffer, plan.Phis.Any(phi => phi.Block == new StructuredBlockId(3)));
        Assert.Equal([0, 4], Stored(plan, 6));
    }

    // Where every path into a join holds the same value, the join's call stores nothing.
    [Fact]
    public void AJoinWhosePathsHoldTheSameValuesStoresNothing()
    {
        var method = BlockMethods.Of(
            BlockMethods.Block(0, [I(0, CilOperation.Nop), I(1, CilOperation.Nop)], new StructuredConditionalExit(
                new(2, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            BlockMethods.Block(1, [I(3, CilOperation.Nop)], new StructuredBranchExit(4, new(3))),
            BlockMethods.Block(2, [I(5, CilOperation.Nop)], new StructuredFallthroughExit(new(3))),
            BlockMethods.Block(3, [I(6, CilOperation.Nop), I(7, CilOperation.Nop)], new StructuredTerminalExit(I(8, CilOperation.Return))));

        var plan = Plan(method, [Write(0, 0), Read(7, 0)], [Call(1), Call(6)]);

        Assert.DoesNotContain(plan.Phis, phi => phi.Block == new StructuredBlockId(3));
        Assert.Empty(Stored(plan, 6));
    }

    private static ManagedAggregateSsaPlan Plan(
        StructuredMethod method,
        ImmutableArray<ManagedAggregateLaneAccess> accesses,
        ImmutableArray<ManagedAggregateMaterialization> materializations)
    {
        var blocks = method.Blocks.Values
            .SelectMany(block => block.Instructions.Select(instruction => (instruction.Offset, block.Id)))
            .ToDictionary(pair => pair.Offset, pair => pair.Id);
        var storage = new ManagedAggregateStorage(Pair,
            CliTypeIdentity.Named(new AssemblyIdentity("Tests"), "Tests", "Pair", isValueType: true), 8, Owned: true);
        var analysis = new ManagedAggregateValueAnalysis(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(Pair, storage),
            accesses.ToImmutableDictionary(access => access.InstructionOffset, access => access with { Block = blocks[access.InstructionOffset] }),
            [], [], materializations);
        return new ManagedAggregateSsaPlanner().Plan(new(method, analysis));
    }

    private static int[] Stored(ManagedAggregateSsaPlan plan, int offset) =>
        [.. plan.Materializations.Single(materialization => materialization.Materialization.InstructionOffset == offset)
            .Values.Select(value => value.Lane.ByteOffset)];

    private static ManagedAggregateLaneAccess Write(int offset, int byteOffset) => Access(offset, byteOffset, writes: true);

    private static ManagedAggregateLaneAccess Read(int offset, int byteOffset) => Access(offset, byteOffset, writes: false);

    private static ManagedAggregateLaneAccess Access(int offset, int byteOffset, bool writes) =>
        new(offset, default, new(Pair, byteOffset, CliTypeIdentity.Primitive("i4", CliValueKind.I4)), sizeof(int), CliValueKind.I4, writes);

    private static ManagedAggregateMaterialization Call(int offset, bool reload = true) => new(offset, Pair, reload);
}
