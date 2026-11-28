// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using System.Collections.Immutable;
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
            new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(2)).Structure(validated))).ToArray();

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

        Assert.Null(new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(2)).Structure(Validate(body)));
        Assert.Contains(
            Descendants(StructureWith(body, DispatcherEliminationPolicy.Reducible).Body),
            region => region is StructuredDispatcher);
    }

    // Labels cannot express the transfers of an exception region, so a method
    // with one keeps whatever structure the dispatcher gave it.
    [Fact]
    public void DeclinesAMethodWithAnExceptionRegion()
    {
        var body = Body(CliValueKind.Void, 0, [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.Nop),
            I(3, CilOperation.EndFinally),
            I(4, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 2, null, null)],
        };

        Assert.Null(new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(2)).Structure(Validate(body)));
    }

    // A protected block whose loop a resumption edge enters at its head and jumps into the middle of, as a lowered
    // runtime-async method's is, and which two branches leave: the structurer that needs a single loop exit routes it
    // through a dispatcher, the reducible one places the protected block's blocks with labels.
    [Fact]
    public void LabelsAProtectedBlockThatNeededADispatcher()
    {
        var body = ResumedLoopInAProtectedBlockBody();
        Assert.Contains(
            AllSequences(StructureWith(body, DispatcherEliminationPolicy.Disabled)).SelectMany(Descendants),
            region => region is StructuredDispatcher);

        var method = StructureWith(body, DispatcherEliminationPolicy.Reducible);
        var protectedRegions = method.ExceptionGroups.Values
            .SelectMany(group => group.ProtectedParts.OfType<StructuredExceptionCode>())
            .SelectMany(code => Descendants(code.Body))
            .ToArray();

        Assert.DoesNotContain(AllSequences(method).SelectMany(Descendants), region => region is StructuredDispatcher);
        Assert.Contains(protectedRegions, region => region is StructuredLoop or StructuredLabeledLoop);
        var owners = AllSequences(method).SelectMany(Occurrences)
            .Where(occurrence => occurrence.Role == StructuredBlockRole.Owner)
            .GroupBy(occurrence => occurrence.Block)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(method.Blocks.Count, owners.Count);
        Assert.All(owners.Values, count => Assert.Equal(1, count));
    }

    // A range whose entry does not reach every reachable block of it cannot be placed on its own.
    [Fact]
    public void DeclinesARangeItsEntryDoesNotCover()
    {
        var validated = Validate(ResumedLoopInAProtectedBlockBody());
        var protectedBlocks = validated.Graph.Blocks
            .Where(block => block.StartOffset is >= 6 and < 26)
            .Select(block => block.Index)
            .ToImmutableHashSet<int>();
        var structurer = new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(2));

        Assert.NotNull(structurer.Structure(validated, new StructuringRange(validated.Graph.GetBlockAtOffset(6).Index, protectedBlocks)));
        Assert.Null(structurer.Structure(validated, new StructuringRange(validated.Graph.GetBlockAtOffset(24).Index, protectedBlocks)));
    }

    // A finally handler leaves by falling out of its end, which emission writes no branch for, so a handler whose
    // endfinally labels could place inside an arm (a guard that skips the rest of it) keeps its structure.
    [Fact]
    public void DeclinesAFinallyHandlerRange()
    {
        var body = Body(CliValueKind.I4, 1, [CliValueKind.I4],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
            I(4, CilOperation.EndFinally),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(6, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(7, CilOperation.EndFinally),
            I(8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(9, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 2, 2, 6, null, null)],
        };
        var validated = Validate(body);
        var handler = validated.Graph.Blocks
            .Where(block => block.StartOffset is >= 2 and < 8)
            .Select(block => block.Index)
            .ToImmutableHashSet<int>();

        Assert.Null(new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), new ElseIfChainPolicy(2))
            .Structure(validated, new StructuringRange(validated.Graph.GetBlockAtOffset(2).Index, handler)));
    }

    [Fact]
    public void RejectsAMissingConditionalBranchClassifierOrChainPolicy()
    {
        Assert.Equal("conditionalBranches",
            Assert.Throws<ArgumentNullException>(() =>
                new ReducibleControlFlowStructurer(null!, new ElseIfChainPolicy(2))).ParamName);
        Assert.Equal("elseIfChains",
            Assert.Throws<ArgumentNullException>(() =>
                new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier(), null!)).ParamName);
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

    // i = 0; state = n & 1;
    // try {
    //   head: if (state != 0) goto resumed;          (the resumption edge, into the loop's body)
    //         if (i >= n) leave done;
    //         if (i == 7) leave suspended;
    //   resumed: i++; state = 0; goto head;
    // } finally { }
    // done: return i; suspended: return -1;
    private static CilMethodBody ResumedLoopInAProtectedBlockBody() =>
        Body(
            CliValueKind.I4,
            2,
            [CliValueKind.I4, CliValueKind.I4],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.BitwiseAnd),
            I(5, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(6, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(7, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(9)),
            I(8, CilOperation.Branch, new CilOperand.BranchTarget(17)),
            I(9, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(10, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(11, CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(24)),
            I(12, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(13, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            I(14, CilOperation.BranchIfEqual, new CilOperand.BranchTarget(25)),
            I(15, CilOperation.Nop),
            I(16, CilOperation.Nop),
            I(17, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(18, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(19, CilOperation.Add),
            I(20, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(21, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(22, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(23, CilOperation.Branch, new CilOperand.BranchTarget(6)),
            I(24, CilOperation.Leave, new CilOperand.BranchTarget(28)),
            I(25, CilOperation.Leave, new CilOperand.BranchTarget(30)),
            I(26, CilOperation.Nop),
            I(27, CilOperation.EndFinally),
            I(28, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(29, CilOperation.Return),
            I(30, CilOperation.LoadInt32, new CilOperand.ConstantI4(-1)),
            I(31, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 6, 20, 26, 2, null, null)],
        };

    // The method's body and every sequence its exception groups hold.
    private static IEnumerable<StructuredSequence> AllSequences(StructuredMethod method) =>
        [method.Body, .. method.ExceptionGroups.Values.SelectMany(group => (IEnumerable<StructuredSequence>)[
            .. group.ProtectedParts.OfType<StructuredExceptionCode>().Select(code => code.Body),
            .. group.Clauses.Select(clause => clause.HandlerBody),
            .. group.Clauses.Where(clause => clause.FilterBody is not null).Select(clause => clause.FilterBody!),
            .. group.NormalContinuations.Select(continuation => continuation.Body)])];

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
