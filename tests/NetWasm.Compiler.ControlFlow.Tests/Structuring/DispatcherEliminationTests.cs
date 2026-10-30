using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class DispatcherEliminationTests
{
    // A branch from the inner loop to the code after both loops leaves two
    // loops at once through the labeled block that names its target, and
    // both headers test whether to enter, so both loops can be rotated.
    [Fact]
    public void LeavesNestedLoopsThroughALabeledBlock()
    {
        var validated = Validate(NestedLoopsWithAnEarlyExitBody());
        var found = validated.Graph.GetBlockAtOffset(27).Index;

        var regions = DraftDescendants(Assert.IsType<StructuredSequenceDraft>(
            new ReducibleControlFlowStructurer().Structure(validated))).ToArray();

        Assert.DoesNotContain(regions, region => region is StructuredDispatcherDraft);
        Assert.Equal(2, regions.OfType<StructuredLoopDraft>().Count(loop => loop.ConditionBlock is not null));
        Assert.Contains(regions, region => region is StructuredLabeledBlockDraft labeled && labeled.NextBlock == found);
        Assert.Contains(regions, region => region is StructuredLabelBranchDraft branch && branch.TargetBlock == found);
        var blocks = regions.Select(region => region switch
        {
            StructuredBlockDraft block => block.Block.Index,
            StructuredIfDraft conditional => conditional.ConditionBlock.Index,
            StructuredLoopDraft loop => loop.ConditionBlock!.Index,
            _ => -1,
        }).Where(index => index >= 0).ToArray();
        Assert.Equal(validated.Graph.ReachableBlocks.Order(), blocks.Order());
    }

    [Fact]
    public void LabelsTheBranchOverlapThatNeededADispatcher()
    {
        var body = ExtendedLoopOwnershipTests.BranchOverlapWithContainedLoopBody();
        Assert.Contains(Descendants(Structurize(body).Body), region => region is StructuredDispatcher);

        var method = StructureWith(body, DispatcherEliminationPolicy.Reducible);
        var regions = Descendants(method.Body).ToArray();

        Assert.DoesNotContain(regions, region => region is StructuredDispatcher);
        Assert.Single(regions, region => region is StructuredLoop or StructuredLabeledLoop);
        AssertEveryBlockOwnedOnce(method);
    }

    // Two edges enter the cycle between the blocks at 2 and 6, so neither
    // dominates the other and only a dispatcher can route it.
    [Fact]
    public void KeepsTheDispatcherForIrreducibleControlFlow()
    {
        var body = IrreducibleCycleBody();

        Assert.Null(new ReducibleControlFlowStructurer().Structure(Validate(body)));
        Assert.Contains(
            Descendants(StructureWith(body, DispatcherEliminationPolicy.Reducible).Body),
            region => region is StructuredDispatcher);
    }

    [Fact]
    public void DisabledPolicyKeepsTheDispatcher() =>
        Assert.Contains(
            Descendants(StructureWith(ExtendedLoopOwnershipTests.BranchOverlapWithContainedLoopBody(), DispatcherEliminationPolicy.Disabled).Body),
            region => region is StructuredDispatcher);

    private static StructuredMethod StructureWith(CilMethodBody body, DispatcherEliminationPolicy policy) =>
        new ValidatedStructuredMethodBuilderFactory()
            .Create(dispatcherElimination: policy)
            .Build(Validate(body));

    private static StructuredBlockId BlockAt(StructuredMethod method, int offset) =>
        Assert.Single(method.Blocks, block => block.Value.StartOffset == offset).Key;

    private static void AssertEveryBlockOwnedOnce(StructuredMethod method)
    {
        var owners = Occurrences(method.Body)
            .Where(occurrence => occurrence.Role == StructuredBlockRole.Owner)
            .GroupBy(occurrence => occurrence.Block)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(method.Blocks.Count, owners.Count);
        Assert.All(owners.Values, count => Assert.Equal(1, count));
        Assert.DoesNotContain(Occurrences(method.Body), occurrence => occurrence.Role != StructuredBlockRole.Owner);
    }

    // for (i = 0; i < n; i++) for (j = 0; j < 3; j++) if (i + j == n) return i * j;
    // return -1;
    private static CilMethodBody NestedLoopsWithAnEarlyExitBody() =>
        Body(
            CliValueKind.I4,
            2,
            [CliValueKind.I4, CliValueKind.I4],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(25)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(6, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(7, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(9, CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(20)),
            I(10, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(11, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(12, CilOperation.Add),
            I(13, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(14, CilOperation.BranchIfEqual, new CilOperand.BranchTarget(27)),
            I(15, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(16, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(17, CilOperation.Add),
            I(18, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(19, CilOperation.Branch, new CilOperand.BranchTarget(7)),
            I(20, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(21, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(22, CilOperation.Add),
            I(23, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(24, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(25, CilOperation.LoadInt32, new CilOperand.ConstantI4(-1)),
            I(26, CilOperation.Return),
            I(27, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(28, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(29, CilOperation.Multiply),
            I(30, CilOperation.Return));

    private static CilMethodBody IrreducibleCycleBody() =>
        Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(10)),
            I(4, CilOperation.BranchIfGreaterThanSigned, new CilOperand.BranchTarget(11)),
            I(5, CilOperation.Branch, new CilOperand.BranchTarget(6)),
            I(6, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(8, CilOperation.Add),
            I(9, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(10, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(11, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(12, CilOperation.Return));

    private static IEnumerable<StructuredRegionDraft> DraftDescendants(StructuredSequenceDraft sequence)
    {
        foreach (var region in sequence.Regions)
        {
            yield return region;
            IEnumerable<StructuredSequenceDraft> children = region switch
            {
                StructuredIfDraft conditional => [conditional.WhenTrue, conditional.WhenFalse],
                StructuredLoopDraft loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                StructuredLabeledBlockDraft labeled => [labeled.Body],
                StructuredLabeledLoopDraft labeled => [labeled.Body],
                _ => [],
            };
            foreach (var child in children.SelectMany(DraftDescendants))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<StructuredRegion> Descendants(StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            yield return region;
            foreach (var child in Children(region).SelectMany(Descendants))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<StructuredBlockOccurrence> Occurrences(StructuredSequence sequence)
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
            foreach (var occurrence in Children(region).SelectMany(Occurrences))
            {
                yield return occurrence;
            }
        }
    }

    private static IEnumerable<StructuredSequence> Children(StructuredRegion region) => region switch
    {
        StructuredIf conditional => [conditional.WhenTrue, conditional.WhenFalse],
        StructuredLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
        StructuredPostTestLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
        StructuredDispatcher dispatcher => dispatcher.Exits.Select(exit => exit.Body),
        StructuredLabeledBlock labeled => [labeled.Body],
        StructuredLabeledLoop labeled => [labeled.Body],
        _ => [],
    };
}
