using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// A conditional branch to the next instruction reaches one block whether taken
// or not, and the graph keeps its two edges as one. It still consumes its
// condition, so every structurer places it as an if whose arms are empty. A
// Debug build emits one for an if whose body calls a partial method that has no
// implementation (issue 221).
public sealed class ConditionalBranchToFallthroughTests
{
    public static TheoryData<CilOperation> Operations() =>
    [
        CilOperation.BranchIfTrue, CilOperation.BranchIfFalse,
        CilOperation.BranchIfEqual, CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanSigned, CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualSigned, CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanSigned, CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualSigned, CilOperation.BranchIfLessThanOrEqualUnsigned,
    ];

    [Theory]
    [MemberData(nameof(Operations))]
    public void TheGraphKeepsOneEdgeThatBothTargetsName(CilOperation operation)
    {
        var graph = Validate(Straight(operation)).Graph;
        var block = graph.GetBlockAtOffset(0).Index;

        Assert.Single(graph.Successors[block]);
        Assert.Equal((block + 1, block + 1), graph.GetConditionalTargets(block));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void StraightLineCodeHasAnIfWithEmptyArms(CilOperation operation) =>
        AssertEmptyIf(Straight(operation), operation);

    [Theory]
    [MemberData(nameof(Operations))]
    public void ALoopBodyHasAnIfWithEmptyArms(CilOperation operation) =>
        AssertEmptyIf(InLoop(operation), operation);

    [Theory]
    [MemberData(nameof(Operations))]
    public void ATryRegionHasAnIfWithEmptyArms(CilOperation operation) =>
        AssertEmptyIf(InTry(operation), operation);

    // A cycle entered at two blocks needs a dispatcher, whose block for the
    // branch selects the same next block either way.
    [Theory]
    [MemberData(nameof(Operations))]
    public void ADispatcherSelectsTheSameBlockEitherWay(CilOperation operation)
    {
        foreach (var policy in new[] { DispatcherEliminationPolicy.Disabled, DispatcherEliminationPolicy.Reducible })
        {
            var method = StructureWith(InIrreducibleCycle(operation), policy);
            var dispatcher = Assert.Single(Descendants(method, method.Body).OfType<StructuredDispatcher>());
            var block = Assert.Single(dispatcher.Blocks,
                block => BranchesToItsFallthrough(method, block.Occurrence.Block, operation) &&
                    method.Blocks[block.Occurrence.Block].StartOffset == 6);
            Assert.NotNull(block.WhenTrue);
            Assert.Equal(block.WhenTrue, block.WhenFalse);
        }
    }

    // The labeled-block structurer places a method without a dispatcher's
    // help the same way.
    [Theory]
    [MemberData(nameof(Operations))]
    public void TheLabeledStructurerHasAnIfWithEmptyArms(CilOperation operation)
    {
        var body = Assert.IsType<StructuredSequenceDraft>(
            new ReducibleControlFlowStructurer(new CilConditionalBranchClassifier()).Structure(Validate(InLoop(operation))));
        var conditionals = DraftDescendants(body).OfType<StructuredIfDraft>()
            .Where(conditional => conditional.ConditionBlock.Terminator.Operation == operation &&
                conditional.ConditionBlock.StartOffset != 2)
            .ToArray();

        var conditional = Assert.Single(conditionals);
        Assert.Empty(conditional.WhenTrue.Regions);
        Assert.Empty(conditional.WhenFalse.Regions);
    }

    [Fact]
    public void ABlockWithoutTheEdgesOfAConditionalBranchHasNoConditionalTargets()
    {
        var graph = Validate(Body(CliValueKind.Void, 1, [], [I(0, CilOperation.Return)])).Graph;

        Assert.Throws<InvalidOperationException>(() => graph.GetConditionalTargets(0));
    }

    private static void AssertEmptyIf(CilMethodBody body, CilOperation operation)
    {
        foreach (var policy in new[] { DispatcherEliminationPolicy.Disabled, DispatcherEliminationPolicy.Reducible })
        {
            var method = StructureWith(body, policy);
            var conditional = Assert.Single(Descendants(method, method.Body).OfType<StructuredIf>(),
                conditional => BranchesToItsFallthrough(method, conditional.Condition.Block, operation));
            Assert.Empty(conditional.WhenTrue.Regions);
            Assert.Empty(conditional.WhenFalse.Regions);
        }
    }

    // The block's exit names one block whether the branch is taken or not.
    private static bool BranchesToItsFallthrough(StructuredMethod method, StructuredBlockId block, CilOperation operation) =>
        method.Blocks[block].Exit is StructuredConditionalExit exit &&
        exit.Condition.Operation == operation && exit.WhenTaken == exit.WhenNotTaken;

    private static bool IsBinary(CilOperation operation) =>
        operation is not (CilOperation.BranchIfTrue or CilOperation.BranchIfFalse);

    // The condition's operands from the offset on, and how many there are.
    private static IEnumerable<CilInstruction> Operands(CilOperation operation, int offset)
    {
        yield return I(offset, CilOperation.LoadArgument, new CilOperand.Index(0));
        if (IsBinary(operation)) yield return I(offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(3));
    }

    // if (argument ? 3) {} return argument;
    private static CilMethodBody Straight(CilOperation operation)
    {
        var b = IsBinary(operation) ? 1 : 0;
        return Body(CliValueKind.I4, 2, [],
        [
            .. Operands(operation, 0),
            I(1 + b, operation, new CilOperand.BranchTarget(2 + b)),
            I(2 + b, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3 + b, CilOperation.Return),
        ]);
    }

    // for (i = 0; i < argument; i++) { if (argument ? 3) {} } return i;
    private static CilMethodBody InLoop(CilOperation operation)
    {
        var b = IsBinary(operation) ? 1 : 0;
        return Body(CliValueKind.I4, 2, [CliValueKind.I4],
        [
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.BranchIfGreaterThanOrEqualSigned, new CilOperand.BranchTarget(12 + b)),
            .. Operands(operation, 5),
            I(6 + b, operation, new CilOperand.BranchTarget(7 + b)),
            I(7 + b, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(8 + b, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(9 + b, CilOperation.Add),
            I(10 + b, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(11 + b, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(12 + b, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(13 + b, CilOperation.Return),
        ]);
    }

    // try { if (argument ? 3) {} } finally {} return argument;
    private static CilMethodBody InTry(CilOperation operation)
    {
        var b = IsBinary(operation) ? 1 : 0;
        return Body(CliValueKind.I4, 2, [],
        [
            .. Operands(operation, 0),
            I(1 + b, operation, new CilOperand.BranchTarget(2 + b)),
            I(2 + b, CilOperation.Leave, new CilOperand.BranchTarget(5 + b)),
            I(3 + b, CilOperation.Nop),
            I(4 + b, CilOperation.EndFinally),
            I(5 + b, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(6 + b, CilOperation.Return),
        ]) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 3 + b, 3 + b, 2, null, null)],
        };
    }

    // Two edges enter the cycle between the blocks at 2 and 6, and the block
    // at 6 branches to the next instruction.
    private static CilMethodBody InIrreducibleCycle(CilOperation operation)
    {
        var b = IsBinary(operation) ? 1 : 0;
        return Body(CliValueKind.I4, 2, [],
        [
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(10)),
            I(4, CilOperation.BranchIfGreaterThanSigned, new CilOperand.BranchTarget(13 + b)),
            I(5, CilOperation.Branch, new CilOperand.BranchTarget(6)),
            .. Operands(operation, 6),
            I(7 + b, operation, new CilOperand.BranchTarget(8 + b)),
            I(8 + b, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(9 + b, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(10 + b, CilOperation.Add),
            I(11 + b, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(12 + b, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(13 + b, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(14 + b, CilOperation.Return),
        ]);
    }

    private static CilMethodBody Body(
        CliValueKind result,
        int maxStack,
        ImmutableArray<CliValueKind> locals,
        CilInstruction[] instructions) =>
        ControlFlowTestSupport.Body(result, maxStack, locals, instructions);

    private static StructuredMethod StructureWith(CilMethodBody body, DispatcherEliminationPolicy policy) =>
        new ValidatedStructuredMethodBuilderFactory()
            .Create(dispatcherElimination: policy)
            .Build(Validate(body));

    private static IEnumerable<StructuredRegion> Descendants(StructuredMethod method, StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            yield return region;
            IEnumerable<StructuredSequence> children = region switch
            {
                StructuredIf conditional => [conditional.WhenTrue, conditional.WhenFalse],
                StructuredLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                StructuredPostTestLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                StructuredDispatcher dispatcher => dispatcher.Exits.Select(exit => exit.Body),
                StructuredLabeledBlock labeled => [labeled.Body],
                StructuredLabeledLoop labeled => [labeled.Body],
                StructuredExceptionRegion exception =>
                [
                    .. method.ExceptionGroups[exception.Group].ProtectedParts.OfType<StructuredExceptionCode>()
                        .Select(part => part.Body),
                    .. method.ExceptionGroups[exception.Group].Clauses.Select(clause => clause.HandlerBody),
                ],
                _ => [],
            };
            foreach (var child in children.SelectMany(child => Descendants(method, child)))
            {
                yield return child;
            }
        }
    }

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
}
