using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayBoundsCheckEliminatorTests
{
    [Fact]
    public void RequiresDependenciesAndInputBeforeAnalysis()
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var collaborators = new RecordingAnalysis(flow, []);
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(null!, collaborators, collaborators, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(collaborators, null!, collaborators, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(collaborators, collaborators, null!, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, true).Rewrite(null!));
        Assert.Empty(collaborators.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledPolicyClearsPreviousProofWithoutAnalysis(bool hadProof)
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var body = flow.Graph.MethodBody;
        if (hadProof) body = body with
        {
            ArrayBoundsProof = new(body.Instructions, body.Locals, body.LocalSignatureTypes, [7]),
        };
        var collaborators = new RecordingAnalysis(flow, [7]);
        var actor = new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, false);
        var result = actor.Rewrite(body);
        Assert.Null(result.ArrayBoundsProof);
        Assert.Empty(collaborators.Events);
        Assert.Equal(body.Instructions, result.Instructions);
        if (!hadProof) Assert.Same(body, result);
    }

    [Fact]
    public void RecomputesProofAfterClearingPreviousFacts()
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var body = flow.Graph.MethodBody;
        body = body with
        {
            ArrayBoundsProof = new(body.Instructions, body.Locals, body.LocalSignatureTypes, [999]),
        };
        var collaborators = new RecordingAnalysis(flow, [7]);
        var actor = new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, true);
        var result = actor.Rewrite(body);
        Assert.Equal(["graph", "validate", "plan"], collaborators.Events);
        Assert.NotNull(result.ArrayBoundsProof);
        Assert.Equal([7], result.ArrayBoundsProof.AccessOffsets.ToArray());
        Assert.Equal(body.Instructions, result.ArrayBoundsProof.Instructions);
        Assert.Equal(body.LocalSignatureTypes, result.ArrayBoundsProof.LocalSignatureTypes);
        Assert.Equal(body.Locals, result.ArrayBoundsProof.Locals);
    }

    [Fact]
    public void EmptyProofPreservesBodyIdentity()
    {
        var flow = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow;
        var collaborators = new RecordingAnalysis(flow, []);
        var actor = new ArrayBoundsCheckEliminator(collaborators, collaborators, collaborators, true);
        Assert.Same(flow.Graph.MethodBody, actor.Rewrite(flow.Graph.MethodBody));
        Assert.Equal(["graph", "validate", "plan"], collaborators.Events);
    }

    // index < length always branches, so its loads and the throw it skips go;
    // index >= length never does, so only its loads go.
    [Theory]
    [InlineData(CilOperation.BranchIfLessThanUnsigned)]
    [InlineData(CilOperation.BranchIfLessThanSigned)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned)]
    public void AProvenCheckGoesWithItsLoadsAndTheThrowOnlyItReaches(CilOperation check)
    {
        var taken = check is CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned;
        var body = Body([], [
            .. Prologue,
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadLocal, new CilOperand.Index(1)),
            (check, new CilOperand.BranchTarget(9)),
            .. (taken ? Throw : Result),
            .. (taken ? Result : Throw)]);

        CilOperation[] expected = taken
            ? [.. PrologueOperations, .. ResultOperations]
            : [.. PrologueOperations, .. ResultOperations, .. ThrowOperations];
        Assert.Equal(expected, Operations(Fold(body)));
    }

    // The loads of the index, plus a constant, and of the length go with the
    // check; any other operands, or loads another branch arrives between, stay
    // for the check to pop.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void TheCheckTakesOnlyItsOwnIndexAndLengthLoads(int shape, bool dropped)
    {
        (CilOperation, CilOperand)[] operands = shape switch
        {
            0 => [(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.Nop, new CilOperand.None()),
                (CilOperation.LoadLocal, new CilOperand.Index(1))],
            1 => [(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (CilOperation.Add, new CilOperand.None()), (CilOperation.LoadLocal, new CilOperand.Index(1))],
            2 => [(CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadLocal, new CilOperand.Index(1))],
            3 => [(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (CilOperation.Subtract, new CilOperand.None()), (CilOperation.LoadLocal, new CilOperand.Index(1))],
            4 => [(CilOperation.LoadLocal, new CilOperand.Index(0)), (CilOperation.LoadLocal, new CilOperand.Index(0)),
                (CilOperation.Add, new CilOperand.None()), (CilOperation.LoadLocal, new CilOperand.Index(1))],
            _ => [(CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (CilOperation.Add, new CilOperand.None()), (CilOperation.LoadLocal, new CilOperand.Index(1))],
        };
        var check = Prologue.Length + operands.Length;
        var body = Body([], [
            .. Prologue, .. operands,
            (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(check + 3)),
            .. Throw, .. Result]);

        CilOperation[] kept = [.. operands.Select(static operand => operand.Item1).Where(static operation => operation != CilOperation.Nop),
            CilOperation.Pop, CilOperation.Pop];
        CilOperation[] expected = dropped
            ? [.. PrologueOperations, .. ResultOperations]
            : [.. PrologueOperations, .. kept, .. ResultOperations];
        Assert.Equal(expected, Operations(Fold(body)));
    }

    [Fact]
    public void LoadsAnotherBranchArrivesBetweenStayForTheCheckToPop()
    {
        var body = Body([], [
            .. Prologue,
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadArgument, new CilOperand.Index(2)),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(9)),
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadLocal, new CilOperand.Index(1)),
            (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(13)),
            .. Throw, .. Result]);

        CilOperation[] expected =
        [
            .. PrologueOperations, CilOperation.LoadLocal, CilOperation.LoadArgument, CilOperation.BranchIfTrue,
            CilOperation.Pop, CilOperation.LoadLocal, CilOperation.LoadLocal, CilOperation.Pop, CilOperation.Pop,
            .. ResultOperations,
        ];
        Assert.Equal(expected, Operations(Fold(body)));
    }

    // A taken check becomes a branch, which stays where it goes back, skips
    // what another branch also reaches, or lands where another branch does.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ATakenCheckStaysABranchWhereItsSkipIsSharedOrBackward(int shape)
    {
        var body = shape switch
        {
            0 => Body([], [
                .. Prologue,
                (CilOperation.LoadLocal, new CilOperand.Index(0)),
                (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (CilOperation.Add, new CilOperand.None()),
                (CilOperation.StoreLocal, new CilOperand.Index(0)),
                (CilOperation.LoadLocal, new CilOperand.Index(0)),
                (CilOperation.LoadLocal, new CilOperand.Index(1)),
                (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(4)),
                .. Result]),
            1 => Body([], [
                .. Prologue,
                (CilOperation.LoadLocal, new CilOperand.Index(0)),
                (CilOperation.LoadLocal, new CilOperand.Index(1)),
                (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(9)),
                .. Throw,
                (CilOperation.LoadArgument, new CilOperand.Index(2)),
                (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(7)),
                .. Result]),
            _ => Body([], [
                .. Prologue,
                (CilOperation.LoadArgument, new CilOperand.Index(2)),
                (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(11)),
                (CilOperation.LoadLocal, new CilOperand.Index(0)),
                (CilOperation.LoadLocal, new CilOperand.Index(1)),
                (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(11)),
                .. Throw, .. Result]),
        };

        CilOperation[] expected = shape switch
        {
            0 => [.. PrologueOperations, CilOperation.LoadLocal, CilOperation.LoadInt32, CilOperation.Add, CilOperation.StoreLocal,
                CilOperation.Branch, .. ResultOperations],
            1 => [.. PrologueOperations, CilOperation.Branch, .. ThrowOperations, CilOperation.LoadArgument,
                CilOperation.BranchIfTrue, .. ResultOperations],
            _ => [.. PrologueOperations, CilOperation.LoadArgument, CilOperation.BranchIfTrue, CilOperation.Branch,
                .. ThrowOperations, .. ResultOperations],
        };
        Assert.Equal(expected, Operations(Fold(body)));
    }

    // Protected regions keep their bounds over the folded check, whose try
    // entry another path reaches; a filter's start is one more such entry.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AProvenCheckInAProtectedRegionFolds(bool filter)
    {
        (CilOperation, CilOperand)[] tail = filter
            ? [(CilOperation.Leave, new CilOperand.BranchTarget(15)),
                (CilOperation.Pop, new CilOperand.None()), (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                (CilOperation.EndFilter, new CilOperand.None()),
                (CilOperation.Pop, new CilOperand.None()), (CilOperation.Leave, new CilOperand.BranchTarget(15)),
                .. Result]
            : [(CilOperation.Leave, new CilOperand.BranchTarget(12)),
                (CilOperation.Pop, new CilOperand.None()), (CilOperation.Leave, new CilOperand.BranchTarget(12)),
                .. Result];
        CilExceptionRegion region = filter
            ? new(CilExceptionRegionKind.Filter, 4, 6, 13, 2, null, 10)
            : new(CilExceptionRegionKind.Catch, 4, 6, 10, 2, new EntityKey(ValidationTestData.Assembly, 0x02000002), null);
        var body = Body([region], [
            .. Prologue,
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadLocal, new CilOperand.Index(1)),
            (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(9)),
            .. Throw, .. tail]);

        var folded = Fold(body);

        CilOperation[] expected = [.. PrologueOperations, .. tail.Select(static instruction => instruction.Item1)];
        Assert.Equal(expected, Operations(folded));
        var protectedRegion = Assert.Single(folded.ExceptionRegions);
        Assert.Equal(CilOperation.Leave, folded.Instructions.Single(instruction => instruction.Offset == protectedRegion.TryOffset + protectedRegion.TryLength - 1).Operation);
    }

    // index, length = arguments 0 and 1.
    private static readonly (CilOperation, CilOperand)[] Prologue =
    [
        (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.StoreLocal, new CilOperand.Index(0)),
        (CilOperation.LoadArgument, new CilOperand.Index(1)), (CilOperation.StoreLocal, new CilOperand.Index(1)),
    ];

    private static readonly (CilOperation, CilOperand)[] Throw =
        [(CilOperation.LoadNull, new CilOperand.None()), (CilOperation.Throw, new CilOperand.None())];

    private static readonly (CilOperation, CilOperand)[] Result =
        [(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), (CilOperation.Return, new CilOperand.None())];

    private static readonly CilOperation[] PrologueOperations = [.. Prologue.Select(static instruction => instruction.Item1)];

    private static readonly CilOperation[] ThrowOperations = [.. Throw.Select(static instruction => instruction.Item1)];

    private static readonly CilOperation[] ResultOperations = [.. Result.Select(static instruction => instruction.Item1)];

    private static CilMethodBody Body(ImmutableArray<CilExceptionRegion> regions, (CilOperation Operation, CilOperand Operand)[] code)
    {
        var definition = new MethodDefinitionModel(
            new(ValidationTestData.Assembly, 0x06000001), new(ValidationTestData.Assembly, 0x02000001), "Check", true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4), 1);
        return new CilMethodBody(definition, 4, [CliValueKind.I4, CliValueKind.I4],
            [.. code.Select((instruction, offset) => new CilInstruction(offset, offset + 1, instruction.Operation, instruction.Operand))])
        {
            LocalSignatureTypes = [CliTypeIdentity.FromStackKind(CliValueKind.I4), CliTypeIdentity.FromStackKind(CliValueKind.I4)],
            ExceptionRegions = regions,
        };
    }

    private static CilMethodBody Fold(CilMethodBody body)
    {
        var analysis = new ProvingAnalysis();
        return new ArrayBoundsCheckEliminator(analysis, analysis, analysis, true).Rewrite(body);
    }

    private static CilOperation[] Operations(CilMethodBody body) =>
        [.. body.Instructions.Select(static instruction => instruction.Operation).Where(static operation => operation != CilOperation.Nop)];

    // Proves every index check of the body it plans.
    private sealed class ProvingAnalysis : IControlFlowGraphBuilder, ITypedStackValidator, IArrayBoundsCheckEliminationPlanner
    {
        private readonly IControlFlowGraphBuilder _graphs = new ControlFlowGraphBuilderFactory().Create();

        public ControlFlowGraph Build(CilMethodBody method) => _graphs.Build(method);

        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) =>
            new(graph, graph.Blocks.ToImmutableDictionary(static block => block.Index, static _ => ImmutableArray<CliValueKind>.Empty),
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty);

        public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow) =>
        [
            .. controlFlow.Graph.MethodBody.Instructions
                .Where(static instruction => instruction.Operation is CilOperation.BranchIfLessThanUnsigned or
                    CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                    CilOperation.BranchIfGreaterThanOrEqualSigned)
                .Select(static instruction => instruction.Offset),
        ];
    }

    private sealed class RecordingAnalysis(ValidatedControlFlowGraph flow, ImmutableHashSet<int> offsets) :
        IControlFlowGraphBuilder, ITypedStackValidator, IArrayBoundsCheckEliminationPlanner
    {
        public List<string> Events { get; } = [];
        public ControlFlowGraph Build(CilMethodBody method)
        {
            Assert.Null(method.ArrayBoundsProof);
            Events.Add("graph");
            return flow.Graph;
        }
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph)
        {
            Assert.Same(flow.Graph, graph);
            Events.Add("validate");
            return flow;
        }
        public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow)
        {
            Assert.Same(flow, controlFlow);
            Events.Add("plan");
            return offsets;
        }
    }
}
