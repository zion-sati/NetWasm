using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExtendedLoopOwnershipTests
{
    [Fact]
    public void BranchOverlapDispatcherPreservesAContainedNaturalLoop()
    {
        var method = Structurize(BranchOverlapWithContainedLoopBody());
        var regions = Descendants(method.Body).ToArray();
        var dispatcher = Assert.Single(regions.OfType<StructuredDispatcher>());
        var loop = Assert.Single(regions, region =>
            region is StructuredLoop or StructuredPostTestLoop);
        var condition = Assert.Single(method.Blocks,
            block => block.Value.StartOffset == 7).Key;
        var body = Assert.Single(method.Blocks,
            block => block.Value.StartOffset == 10).Key;
        var dispatcherBlocks = dispatcher.Blocks
            .Select(block => block.Occurrence.Block)
            .ToHashSet();
        var ownerCounts = Occurrences(method.Body)
            .Where(occurrence => occurrence.Role == StructuredBlockRole.Owner)
            .GroupBy(occurrence => occurrence.Block)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.DoesNotContain(condition, dispatcherBlocks);
        Assert.DoesNotContain(body, dispatcherBlocks);
        Assert.Contains(dispatcher.Exits, exit =>
            Descendants(exit.Body).Contains(loop));
        Assert.Equal(method.Blocks.Count, ownerCounts.Count);
        Assert.All(method.Blocks.Keys,
            block => Assert.Equal(1, ownerCounts[block]));
    }

    [Fact]
    public void SharedExitTailHasOneOwnerAndOneExecutingReplica()
    {
        var method = Structurize(SharedExitTailBody());
        var regions = Descendants(method.Body).ToArray();
        var loop = Assert.Single(regions, region =>
            region is StructuredLoop or StructuredPostTestLoop);
        Assert.DoesNotContain(regions, region => region is StructuredDispatcher);
        var sharedTail = Assert.Single(method.Blocks,
            block => block.Value.StartOffset == 19).Key;
        var occurrences = Occurrences(method.Body)
            .Where(occurrence => occurrence.Block == sharedTail)
            .ToArray();

        Assert.Equal(2, occurrences.Length);
        Assert.Contains(occurrences,
            occurrence => occurrence.Role == StructuredBlockRole.Owner);
        Assert.Contains(occurrences,
            occurrence => occurrence.Role == StructuredBlockRole.ExecutingReplica);
    }

    private static CilMethodBody SharedExitTailBody() =>
        Body(
            CliValueKind.I4,
            2,
            [CliValueKind.I4],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(4, CilOperation.BranchIfLessThanSigned,
                new CilOperand.BranchTarget(19)),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(6, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(7, CilOperation.BranchIfGreaterThanOrEqualSigned,
                new CilOperand.BranchTarget(16)),
            I(8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(10, CilOperation.Add),
            I(11, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(12, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(13, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(14, CilOperation.BranchIfEqual,
                new CilOperand.BranchTarget(19)),
            I(15, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            I(16, CilOperation.Call,
                new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(17, CilOperation.Pop),
            I(18, CilOperation.Branch, new CilOperand.BranchTarget(22)),
            I(19, CilOperation.Call,
                new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(20, CilOperation.Pop),
            I(21, CilOperation.Branch, new CilOperand.BranchTarget(22)),
            I(22, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(23, CilOperation.Return));

    internal static CilMethodBody BranchOverlapWithContainedLoopBody() =>
        Body(
            CliValueKind.I4,
            2,
            [CliValueKind.I4],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue,
                new CilOperand.BranchTarget(5)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.BranchIfTrue,
                new CilOperand.BranchTarget(19)),
            I(4, CilOperation.Branch, new CilOperand.BranchTarget(24)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(6, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(7, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(9, CilOperation.BranchIfGreaterThanOrEqualSigned,
                new CilOperand.BranchTarget(15)),
            I(10, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(11, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(12, CilOperation.Add),
            I(13, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(14, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            I(15, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(16, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(17, CilOperation.BranchIfEqual,
                new CilOperand.BranchTarget(19)),
            I(18, CilOperation.Branch, new CilOperand.BranchTarget(24)),
            I(19, CilOperation.Branch, new CilOperand.BranchTarget(20)),
            I(20, CilOperation.Branch, new CilOperand.BranchTarget(21)),
            I(21, CilOperation.Branch, new CilOperand.BranchTarget(22)),
            I(22, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(23, CilOperation.Return),
            I(24, CilOperation.Branch, new CilOperand.BranchTarget(25)),
            I(25, CilOperation.Branch, new CilOperand.BranchTarget(26)),
            I(26, CilOperation.Branch, new CilOperand.BranchTarget(27)),
            I(27, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(28, CilOperation.Return));

    private static IEnumerable<StructuredRegion> Descendants(
        StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            yield return region;
            foreach (var child in ChildSequences(region))
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static IEnumerable<StructuredBlockOccurrence> Occurrences(
        StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            switch (region)
            {
                case StructuredCode code:
                    yield return code.Occurrence;
                    break;
                case StructuredIf conditional:
                    yield return conditional.Condition;
                    break;
                case StructuredLoop { Condition: { } condition }:
                    yield return condition;
                    break;
                case StructuredPostTestLoop loop:
                    yield return loop.Condition;
                    break;
                case StructuredDispatcher dispatcher:
                    foreach (var block in dispatcher.Blocks)
                    {
                        yield return block.Occurrence;
                    }
                    break;
            }
            foreach (var child in ChildSequences(region))
            {
                foreach (var occurrence in Occurrences(child))
                {
                    yield return occurrence;
                }
            }
        }
    }

    private static IEnumerable<StructuredSequence> ChildSequences(
        StructuredRegion region) => region switch
        {
            StructuredIf conditional =>
                [conditional.WhenTrue, conditional.WhenFalse],
            StructuredLoop loop =>
                [loop.Body, loop.ContinueBody, loop.ExitBody],
            StructuredPostTestLoop loop =>
                [loop.Body, loop.ContinueBody, loop.ExitBody],
            StructuredDispatcher dispatcher =>
                dispatcher.Exits.Select(exit => exit.Body),
            _ => [],
        };
}
