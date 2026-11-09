using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

using Code = (CilOperation Operation, CilOperand? Operand);

// Branches the ranges of their operands settle, on CIL whose ranges come from
// constants through joins, guards and loops: the decision for each comparison,
// what a settled branch becomes, the code it cuts off, and the branches that
// stay. RangeBranchCorpusTests runs the C# shapes against desktop .NET.
public sealed class RangeBranchRewriterTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.FromStackKind(CliValueKind.I4);

    // Ranges a..b and c..d, the second omitted for a branch on one value.
    [Theory]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, 0, 7, 8L, 8L, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, 8, 10, 8L, 8L, true)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, 0, 10, 8L, 8L, null)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned, 4, 9, 3L, 3L, true)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned, 4, 9, 9L, 12L, false)]
    [InlineData(CilOperation.BranchIfLessThanSigned, 0, 3, 4L, 6L, true)]
    [InlineData(CilOperation.BranchIfLessThanSigned, 4, 6, 4L, 4L, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, 0, 4, 4L, 6L, true)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, 5, 6, 0L, 4L, false)]
    [InlineData(CilOperation.BranchIfEqual, 3, 3, 3L, 3L, true)]
    [InlineData(CilOperation.BranchIfEqual, 0, 2, 3L, 4L, false)]
    [InlineData(CilOperation.BranchIfEqual, 0, 3, 3L, 4L, null)]
    [InlineData(CilOperation.BranchIfNotEqual, 3, 3, 3L, 3L, false)]
    [InlineData(CilOperation.BranchIfNotEqual, 0, 2, 3L, 4L, true)]
    [InlineData(CilOperation.BranchIfTrue, 1, 5, null, null, true)]
    [InlineData(CilOperation.BranchIfTrue, 0, 0, null, null, false)]
    [InlineData(CilOperation.BranchIfTrue, 0, 1, null, null, null)]
    [InlineData(CilOperation.BranchIfFalse, 0, 0, null, null, true)]
    [InlineData(CilOperation.BranchIfFalse, -5, -1, null, null, false)]
    [InlineData(CilOperation.BranchIfLessThanUnsigned, 0, 1, 5L, 5L, null)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned, 6, 9, 5L, 5L, null)]
    [InlineData(CilOperation.Add, 0, 1, 5L, 5L, null)]
    public void ABranchIsTakenWhenItsOperandsRangesOnlyAllowThat(
        CilOperation operation, long low, long high, long? otherLow, long? otherHigh, bool? taken)
    {
        // A value below the operands does not take part.
        ImmutableArray<IntegerRange?> stack = otherLow is { } secondLow
            ? [new IntegerRange(-1, -1), new IntegerRange(low, high), new IntegerRange(secondLow, otherHigh!.Value)]
            : [new IntegerRange(-1, -1), new IntegerRange(low, high)];

        Assert.Equal(taken, IntegerRangeAnalysis.Taken(operation, stack));
    }

    [Fact]
    public void ABranchOnAValueThatIsNotAnIntegerGoesEitherWay()
    {
        Assert.Null(IntegerRangeAnalysis.Taken(CilOperation.BranchIfTrue, [null]));
        Assert.Null(IntegerRangeAnalysis.Taken(CilOperation.BranchIfEqual, [new IntegerRange(3, 3), null]));
    }

    // Upstream's scalar fallback: inside `if (length < 8)`, `while (length >= 8)`
    // never runs. Its test falls through, and its body goes.
    [Fact]
    public void ALoopItsGuardRulesOutGoes()
    {
        var body = Body(
            Argument(0), Constant(8), I(CilOperation.BranchIfGreaterThanOrEqualSigned, Target(14)),
            I(CilOperation.Branch, Target(9)),
            Argument(0), Constant(8), I(CilOperation.Subtract), I(CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(CilOperation.Nop),
            Argument(0), Constant(8), I(CilOperation.BranchIfGreaterThanOrEqualSigned, Target(4)),
            Constant(-1), I(CilOperation.Return),
            Constant(1), I(CilOperation.Return));

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            ["ldarg 0", "ldc.i4 8", "bge 15", "nop", "nop", "nop", "nop", "nop", "nop", "ldarg 0", "ldc.i4 8", "pop", "pop",
             "ldc.i4 -1", "ret", "ldc.i4 1", "ret"],
            Text(rewritten));
        _ = Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(rewritten));
    }

    // A local set to 5 on one path and 9 on the other is above 3 where they join;
    // the jump falls through once the code it jumped over goes.
    [Fact]
    public void ABranchItsOperandsAlwaysSatisfyJumps()
    {
        var body = Body(
            Argument(0), I(CilOperation.BranchIfTrue, Target(5)),
            Constant(5), Local(CilOperation.StoreLocal, 0), I(CilOperation.Branch, Target(7)),
            Constant(9), Local(CilOperation.StoreLocal, 0),
            Local(CilOperation.LoadLocal, 0), Constant(3), I(CilOperation.BranchIfGreaterThanSigned, Target(12)),
            Constant(0), I(CilOperation.Return),
            Constant(1), I(CilOperation.Return));

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            ["ldarg 0", "brtrue 5", "ldc.i4 5", "stloc 0", "br 7", "ldc.i4 9", "stloc 0", "ldloc 0", "ldc.i4 3", "pop", "pop",
             "nop", "nop", "nop", "ldc.i4 1", "ret"],
            Text(rewritten));
        _ = Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(rewritten));
    }

    // Both paths leave 0, so a branch on it is never taken.
    [Fact]
    public void ABranchOnOneValueFoldsByItsRange()
    {
        var body = Body(
            Argument(0), I(CilOperation.BranchIfTrue, Target(5)),
            Constant(0), Local(CilOperation.StoreLocal, 0), I(CilOperation.Branch, Target(7)),
            Constant(0), Local(CilOperation.StoreLocal, 0),
            Local(CilOperation.LoadLocal, 0), I(CilOperation.BranchIfTrue, Target(11)),
            Constant(7), I(CilOperation.Return),
            Constant(8), I(CilOperation.Return));

        var rewritten = Rewriter().Rewrite(body);

        Assert.Equal(
            ["ldarg 0", "brtrue 5", "ldc.i4 0", "stloc 0", "br 7", "ldc.i4 0", "stloc 0", "ldloc 0", "pop", "ldc.i4 7", "ret",
             "nop", "ret"],
            Text(rewritten));
    }

    // An argument may hold anything, and a branch after the method returns is never reached.
    [Fact]
    public void BranchesTheirOperandsDoNotSettleStay()
    {
        var body = Body(
            Argument(0), Constant(8), I(CilOperation.BranchIfGreaterThanOrEqualSigned, Target(5)),
            Constant(0), I(CilOperation.Return),
            Constant(1), I(CilOperation.Return),
            Constant(1), Constant(1), I(CilOperation.BranchIfEqual, Target(5)),
            Constant(2), I(CilOperation.Return));

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void ADisabledRewriterOrABodyWithoutConditionalBranchesIsLeftAsItIs()
    {
        var settled = Body(
            Constant(1), I(CilOperation.BranchIfTrue, Target(4)),
            Constant(0), I(CilOperation.Return),
            Constant(1), I(CilOperation.Return));
        var straight = Body(Argument(0), I(CilOperation.Return));
        var graphs = new ControlFlowGraphBuilderFactory().Create();

        Assert.Same(settled, new RangeBranchRewriter(graphs, Validator(), new Unused(), ConstantBranchFoldingFactory.Create(graphs), false)
            .Rewrite(settled));
        Assert.Same(straight, new RangeBranchRewriter(graphs, Validator(), new Unused(), ConstantBranchFoldingFactory.Create(graphs), true)
            .Rewrite(straight));
    }

    [Fact]
    public void TheRewriterRequiresItsInputs()
    {
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var ranges = new IntegerRangeAnalysis(new StackEffectResolver(new Calls()));
        var cutOff = ConstantBranchFoldingFactory.Create(graphs);
        Assert.Throws<ArgumentNullException>(() => new RangeBranchRewriter(null!, Validator(), ranges, cutOff, true));
        Assert.Throws<ArgumentNullException>(() => new RangeBranchRewriter(graphs, null!, ranges, cutOff, true));
        Assert.Throws<ArgumentNullException>(() => new RangeBranchRewriter(graphs, Validator(), null!, cutOff, true));
        Assert.Throws<ArgumentNullException>(() => new RangeBranchRewriter(graphs, Validator(), ranges, null!, true));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    private static RangeBranchRewriter Rewriter()
    {
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        return new(graphs, Validator(), new IntegerRangeAnalysis(new StackEffectResolver(new Calls())),
            ConstantBranchFoldingFactory.Create(graphs), true);
    }

    // Probe(int value): local int.
    private static CilMethodBody Body(params Code[] code)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Probe", true,
            new MethodSignatureModel(Int32, [Int32]), 1);
        return new CilMethodBody(method, 8, [CliValueKind.I4],
            [.. code.Select(static (instruction, index) =>
                new CilInstruction(index, index + 1, instruction.Operation, instruction.Operand ?? new CilOperand.None()))])
        {
            LocalSignatureTypes = [Int32],
        };
    }

    private static List<string> Text(CilMethodBody body) =>
    [
        .. body.Instructions.Select(static instruction => (instruction.Operation, instruction.Operand) switch
        {
            (CilOperation.LoadInt32, CilOperand.ConstantI4 { Value: var value }) => $"ldc.i4 {value}",
            (CilOperation.StoreLocal, CilOperand.Index { Value: var local }) => $"stloc {local}",
            (CilOperation.LoadLocal, CilOperand.Index { Value: var local }) => $"ldloc {local}",
            (CilOperation.LoadArgument, CilOperand.Index { Value: var argument }) => $"ldarg {argument}",
            (CilOperation.Branch, CilOperand.BranchTarget { Offset: var target }) => $"br {target}",
            (CilOperation.BranchIfTrue, CilOperand.BranchTarget { Offset: var target }) => $"brtrue {target}",
            (CilOperation.BranchIfGreaterThanOrEqualSigned, CilOperand.BranchTarget { Offset: var target }) => $"bge {target}",
            (CilOperation.Pop, _) => "pop",
            (CilOperation.Nop, _) => "nop",
            (CilOperation.Return, _) => "ret",
            var other => other.Operation.ToString(),
        }),
    ];

    private static TypedStackValidator Validator() =>
        new(new Repository(), new Repository(), new Repository(), new StackTypeCompatibilityValidator());

    private static Code I(CilOperation operation, CilOperand? operand = null) => (operation, operand);

    private static Code Constant(int value) => (CilOperation.LoadInt32, new CilOperand.ConstantI4(value));

    private static Code Argument(int index) => (CilOperation.LoadArgument, new CilOperand.Index(index));

    private static Code Local(CilOperation operation, int index) => (operation, new CilOperand.Index(index));

    private static CilOperand.BranchTarget Target(int offset) => new(offset);

    private sealed class Unused : IIntegerRangeAnalysis
    {
        public IReadOnlyDictionary<int, ImmutableArray<IntegerRange?>> StacksAt(
            ValidatedControlFlowGraph controlFlow, IReadOnlySet<int> offsets) => throw new InvalidOperationException();
    }

    private sealed class Calls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) =>
            instruction.Operand is CilOperand.MethodInstance method ? method.Value : null;
    }

    private sealed class Repository : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
