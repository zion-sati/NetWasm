using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayLoopUnrollingPlannerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavesReferenceProducingBodiesUnchanged(bool text)
    {
        var fixture = WithPayload([
            new(0, 1, text ? CilOperation.LoadString : CilOperation.LoadNull,
                text ? new CilOperand.UserString("value") : new CilOperand.None()),
            new(1, 2, CilOperation.Pop, new CilOperand.None()),
        ]);
        var proofs = fixture.Analyzer.Analyze(fixture.Flow);
        Assert.Single(proofs);

        Assert.Empty(Planner(new FrozenInduction(proofs), 512).Plan(fixture.Flow));
    }

    [Theory]
    [MemberData(nameof(NumericOperations))]
    public void AdmitsSupportedNumericPayloadsWithoutChangingTheirOrder(CilOperation operation)
    {
        var payload = Payload(operation);
        var fixture = WithPayload(payload);
        var planner = Planner(new FrozenInduction(fixture.Analyzer.Analyze(fixture.Flow)), 512);

        var plan = Assert.Single(planner.Plan(fixture.Flow));

        Assert.Equal(4, plan.Factor);
        Assert.Equal(payload.Select(item => item.Operation), plan.Body.Take(payload.Length).Select(item => item.Operation));
        Assert.Equal(payload.Select(item => item.Operand), plan.Body.Take(payload.Length).Select(item => item.Operand));
        Assert.Equal(payload.Length + 4, plan.Body.Length);
        Assert.Equal(4, Assert.Single(plan.EntryBranches));
    }

    // A span element's address, as Unsafe.Add computes it, and a primitive
    // read or written through it; another call, or a struct read, stays.
    [Theory]
    [InlineData(RuntimeIntrinsic.UnsafeAdd, true)]
    [InlineData(RuntimeIntrinsic.UnsafeAddByteOffset, true)]
    [InlineData(RuntimeIntrinsic.UnsafeAs, true)]
    [InlineData(RuntimeIntrinsic.NativeMemoryCopy, false)]
    public void AdmitsAddressIntrinsicsOnly(RuntimeIntrinsic intrinsic, bool admitted)
    {
        var method = new EntityKey(new AssemblyIdentity("fixture"), 0x06000042);
        var fixture = WithPayload([
            new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            new(1, 2, CilOperation.Call, new CilOperand.Entity(method)),
            new(2, 3, CilOperation.Pop, new CilOperand.None()),
        ]);
        var planner = new ArrayLoopUnrollingPlanner(new FrozenInduction(fixture.Analyzer.Analyze(fixture.Flow)),
            new Intrinsics(method, intrinsic), 512);

        Assert.Equal(admitted, !planner.Plan(fixture.Flow).IsEmpty);
    }

    [Theory]
    [InlineData(CilOperation.LoadObject, true, true)]
    [InlineData(CilOperation.StoreObject, true, true)]
    [InlineData(CilOperation.LoadObject, false, false)]
    public void AdmitsPrimitiveIndirectAccessesOnly(CilOperation operation, bool primitive, bool admitted)
    {
        var type = new CilOperand.TypeIdentity(primitive
            ? CliTypeIdentity.FromStackKind(CliValueKind.I4)
            : CliTypeIdentity.Named(new AssemblyIdentity("fixture"), "Fixture", "Pair", isValueType: true));
        var address = new CilInstruction(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0));
        var fixture = WithPayload(operation == CilOperation.LoadObject
            ? [address, new(1, 2, operation, type), new(2, 3, CilOperation.Pop, new CilOperand.None())]
            : [address, new(1, 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), new(2, 3, operation, type)]);

        Assert.Equal(admitted, !Planner(new FrozenInduction(fixture.Analyzer.Analyze(fixture.Flow)), 512).Plan(fixture.Flow).IsEmpty);
    }

    [Fact]
    public void LeavesLoopsWithALookaheadGuardAlone()
    {
        var fixture = WithPayload(Payload(CilOperation.Add));
        var proofs = fixture.Analyzer.Analyze(fixture.Flow).Select(proof => proof with { Lookahead = 1 }).ToImmutableArray();

        Assert.Empty(Planner(new FrozenInduction(proofs), 512).Plan(fixture.Flow));
    }

    [Fact]
    public void RejectsMissingContractsAndNegativeBudget()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var induction = new FrozenInduction([]);
        Assert.Throws<ArgumentNullException>(() => Planner(null!, 0));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopUnrollingPlanner(induction, null!, 0));
        Assert.Throws<ArgumentNullException>(() => Planner(induction, 0).Plan(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Planner(induction, -1).Plan(fixture.Flow));
    }

    private static ArrayLoopUnrollingPlanner Planner(IArrayLoopInductionAnalyzer induction, int budget) =>
        new(induction, new Intrinsics(default, default), budget);

    private sealed class Intrinsics(EntityKey method, RuntimeIntrinsic value) : IRuntimeIntrinsicRegistry
    {
        public bool TryGetIntrinsic(EntityKey key, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = value;
            return key == method && method != default;
        }
    }

    public static TheoryData<CilOperation> NumericOperations()
    {
        var cases = new TheoryData<CilOperation>();
        CilOperation[] operations = [CilOperation.LoadLocal, CilOperation.StoreLocal, CilOperation.LoadArgument,
            CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32, CilOperation.LoadFloat64,
            CilOperation.Duplicate, CilOperation.Pop, CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply,
            CilOperation.BitwiseAnd, CilOperation.BitwiseOr, CilOperation.BitwiseXor, CilOperation.ShiftLeft,
            CilOperation.ShiftRightSigned, CilOperation.ShiftRightUnsigned, CilOperation.Negate, CilOperation.OnesComplement,
            CilOperation.AddChecked, CilOperation.AddCheckedUnsigned, CilOperation.SubtractChecked, CilOperation.SubtractCheckedUnsigned,
            CilOperation.MultiplyChecked, CilOperation.MultiplyCheckedUnsigned, CilOperation.Divide, CilOperation.DivideUnsigned,
            CilOperation.Remainder, CilOperation.RemainderUnsigned, CilOperation.ConvertInt32, CilOperation.ConvertNumeric,
            CilOperation.LoadArrayLength, CilOperation.LoadArrayElement, CilOperation.StoreArrayElement];
        foreach (var operation in operations) cases.Add(operation);
        return cases;
    }

    private static ImmutableArray<CilInstruction> Payload(CilOperation operation)
    {
        static CilInstruction Instruction(CilOperation op, CilOperand? operand = null) => new(0, 1, op, operand ?? new CilOperand.None());
        var one = Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(1));
        var pop = Instruction(CilOperation.Pop);
        var index = Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(0));
        var array = Instruction(CilOperation.LoadLocal, new CilOperand.Index(1));
        var element = new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        return operation switch
        {
            CilOperation.LoadLocal => [Instruction(operation, new CilOperand.Index(2)), pop],
            CilOperation.StoreLocal => [one, Instruction(operation, new CilOperand.Index(2))],
            CilOperation.LoadArgument => [Instruction(operation, new CilOperand.Index(0)), pop],
            CilOperation.LoadInt32 => [one, pop],
            CilOperation.LoadInt64 => [Instruction(operation, new CilOperand.ConstantI8(1)), pop],
            CilOperation.LoadFloat32 => [Instruction(operation, new CilOperand.ConstantF4(1)), pop],
            CilOperation.LoadFloat64 => [Instruction(operation, new CilOperand.ConstantF8(1)), pop],
            CilOperation.Duplicate => [one, Instruction(operation), pop, pop],
            CilOperation.Pop => [one, pop],
            CilOperation.Negate or CilOperation.OnesComplement or CilOperation.ConvertInt32 => [one, Instruction(operation), pop],
            CilOperation.ConvertNumeric => [one, Instruction(operation, new CilOperand.NumericConversion(32, false, true, false, false)), pop],
            CilOperation.LoadArrayLength => [array, Instruction(operation), pop],
            CilOperation.LoadArrayElement => [array, index, Instruction(operation, element), pop],
            CilOperation.StoreArrayElement => [array, index, one, Instruction(operation, element)],
            _ => [one, one, Instruction(operation), pop],
        };
    }

    private static (IArrayLoopInductionAnalyzer Analyzer, ValidatedControlFlowGraph Flow) WithPayload(
        ImmutableArray<CilInstruction> payload)
    {
        var original = ArrayLoopInductionAnalyzerTests.Create(0, 1).Flow.Graph.MethodBody;
        var guardOffset = 5 + payload.Length + 5;
        var instructions = original.Instructions[..5].AddRange(payload).AddRange(original.Instructions[9..14])
            .AddRange(original.Instructions[14..]);
        var body = original with
        {
            MaxStack = 3,
            Locals = original.Locals.Add(CliValueKind.I4),
            LocalSignatureTypes = original.LocalSignatureTypes.Add(CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            Instructions = [.. instructions.Select((instruction, offset) => instruction with
            {
                Offset = offset,
                NextOffset = offset + 1,
                Operand = instruction.Operand is CilOperand.BranchTarget branch
                    ? new CilOperand.BranchTarget(branch.Offset == 14 ? guardOffset : 5) : instruction.Operand,
            })],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var dominance = new ControlFlowDominanceAnalyzer();
        return (new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance),
            new(graph, graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty), []));
    }

    [Theory]
    [InlineData(26, 4)]
    [InlineData(25, 2)]
    [InlineData(14, 2)]
    public void SelectsFactorWithinExpandedBodyBudget(int budget, int factor)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var planner = Planner(new FrozenInduction(fixture.Analyzer.Analyze(fixture.Flow)), budget);

        var plan = Assert.Single(planner.Plan(fixture.Flow));

        Assert.Equal(factor, plan.Factor);
        Assert.Equal(14, plan.GuardOffset);
        Assert.Equal([4], plan.EntryBranches.ToArray());
        Assert.Equal(8, plan.Body.Length);
        Assert.Equal(0, plan.IndexLocal);
        Assert.Equal(1, plan.ArrayLocal);
        Assert.True(4 * plan.Factor + 2 * (plan.Factor - 1) + 4 <= budget);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void LeavesOversizedLoopUnchanged(int budget)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        Assert.Empty(Planner(new FrozenInduction(fixture.Analyzer.Analyze(fixture.Flow)), budget)
            .Plan(fixture.Flow));
    }

    private sealed class FrozenInduction(ImmutableArray<ArrayLoopInduction> loops) : IArrayLoopInductionAnalyzer
    {
        public ImmutableArray<ArrayLoopInduction> Analyze(ValidatedControlFlowGraph controlFlow) => loops;
    }
}
