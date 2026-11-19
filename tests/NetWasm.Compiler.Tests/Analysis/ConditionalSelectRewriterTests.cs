// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// If-conversion: a branch whose arms only compute a number becomes a select, whether the arms leave the number,
// store it to one local or return it; anything an arm could not run speculatively keeps the branch.
public sealed class ConditionalSelectRewriterTests
{
    private static readonly AssemblyIdentity Fixture = new("fixture");

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        var program = new Program();
        var validator = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
        Assert.Throws<ArgumentNullException>(() => new ConditionalSelectRewriter(null!, validator));
        Assert.Throws<ArgumentNullException>(() =>
            new ConditionalSelectRewriter(new ControlFlowGraphBuilderFactory().Create(), null!));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    // a >= b ? a - b : b - a, as Roslyn compiles it: the arms run first, then the comparison that is true when the
    // branch is not taken, which selects the fallthrough arm.
    [Fact]
    public void AValueDiamondBecomesOneExpressionWithItsOperandsAfterTheArms()
    {
        var body = Body(CliValueKind.I4, [],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadArgument, Arg(1)),
            I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(7)),
            I(3, CilOperation.LoadArgument, Arg(1)), I(4, CilOperation.LoadArgument, Arg(0)), I(5, CilOperation.Subtract),
            I(6, CilOperation.Branch, Target(10)),
            I(7, CilOperation.LoadArgument, Arg(0)), I(8, CilOperation.LoadArgument, Arg(1)), I(9, CilOperation.Subtract),
            I(10, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.Subtract,
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.Subtract,
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.CompareLessThanSigned,
                CilOperation.Select, CilOperation.Return,
            ],
            Operations(result));
        Assert.Equal([1, 0, 0, 1, 0, 1], Arguments(result));
        Assert.Empty(result.Locals);
        Assert.Equal(body.MaxStack + 2, result.MaxStack);
        Assert.All(result.Instructions, instruction => Assert.NotNull(instruction.OriginalOffset));
        _ = Validate(result);
    }

    // if (a < 0) v = -a; and if (c) v = x; else v = y; store one select.
    [Fact]
    public void TrianglesAndStoreDiamondsStoreOneSelect()
    {
        var triangle = Body(CliValueKind.I4, [CliValueKind.I4],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.StoreLocal, Local(0)),
            I(2, CilOperation.LoadLocal, Local(0)), I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(4, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(8)),
            I(5, CilOperation.LoadLocal, Local(0)), I(6, CilOperation.Negate), I(7, CilOperation.StoreLocal, Local(0)),
            I(8, CilOperation.LoadLocal, Local(0)), I(9, CilOperation.Return));

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.StoreLocal,
                CilOperation.LoadLocal, CilOperation.Negate, CilOperation.LoadLocal,
                CilOperation.LoadLocal, CilOperation.LoadInt32, CilOperation.CompareLessThanSigned,
                CilOperation.Select, CilOperation.StoreLocal, CilOperation.LoadLocal, CilOperation.Return,
            ],
            Operations(Rewriter().Rewrite(triangle)));

        var diamond = Body(CliValueKind.I4, [CliValueKind.I4],
            I(0, CilOperation.LoadArgument, Arg(0)),
            I(1, CilOperation.BranchIfTrue, Target(5)),
            I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.StoreLocal, Local(0)),
            I(4, CilOperation.Branch, Target(7)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)), I(6, CilOperation.StoreLocal, Local(0)),
            I(7, CilOperation.LoadLocal, Local(0)), I(8, CilOperation.Return));

        var stored = Rewriter().Rewrite(diamond);

        Assert.Equal(
            [
                CilOperation.LoadInt32, CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.Select,
                CilOperation.StoreLocal, CilOperation.LoadLocal, CilOperation.Return,
            ],
            Operations(stored));
        _ = Validate(stored);
    }

    // if (a > b) return a; return b; returns one select.
    [Fact]
    public void TwoReturnsReturnOneSelect()
    {
        var body = Body(CliValueKind.I4, [],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadArgument, Arg(1)),
            I(2, CilOperation.BranchIfLessThanOrEqualSigned, Target(5)),
            I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.Return),
            I(5, CilOperation.LoadArgument, Arg(1)), I(6, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.LoadArgument,
                CilOperation.CompareGreaterThanSigned, CilOperation.Select, CilOperation.Return,
            ],
            Operations(result));
        _ = Validate(result);
    }

    // A condition computed by a call cannot run after the arms, so it waits in a new int32 local.
    [Fact]
    public void AConditionThatCannotMoveWaitsInALocal()
    {
        var body = Body(CliValueKind.I4, [CliValueKind.I4],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadArgument, Arg(1)),
            I(2, CilOperation.Divide),
            I(3, CilOperation.BranchIfFalse, Target(6)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(5, CilOperation.Return),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(7, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.Divide,
                CilOperation.StoreLocal, CilOperation.LoadInt32, CilOperation.LoadInt32, CilOperation.LoadLocal,
                CilOperation.Select, CilOperation.Return,
            ],
            Operations(result));
        Assert.Equal([CliValueKind.I4, CliValueKind.I4], result.Locals);
        Assert.Equal([1, 1], result.Instructions.Where(instruction => instruction.Operation is CilOperation.StoreLocal or
            CilOperation.LoadLocal).Select(instruction => ((CilOperand.Index)instruction.Operand).Value));
        _ = Validate(result);
    }

    // The unsigned forms of a floating-point branch are the unordered ones, so a NaN takes bge.un, ble.un, bne.un,
    // bgt.un and blt.un and none of the others.
    [Theory]
    [InlineData(CilOperation.BranchIfEqual, CilOperation.CompareEqual, true)]
    [InlineData(CilOperation.BranchIfNotEqual, CilOperation.CompareEqual, false)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned, CilOperation.CompareGreaterThanSigned, true)]
    [InlineData(CilOperation.BranchIfGreaterThanUnsigned, CilOperation.CompareGreaterThanUnsigned, true)]
    [InlineData(CilOperation.BranchIfLessThanSigned, CilOperation.CompareLessThanSigned, true)]
    [InlineData(CilOperation.BranchIfLessThanUnsigned, CilOperation.CompareLessThanUnsigned, true)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, CilOperation.CompareLessThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned, CilOperation.CompareLessThanSigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, CilOperation.CompareGreaterThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned, CilOperation.CompareGreaterThanSigned, false)]
    public void FloatingPointBranchesKeepTheirNaNBehavior(CilOperation branch, CilOperation compare, bool takenWhenTrue)
    {
        var body = Body(CliValueKind.F8, [CliValueKind.F8, CliValueKind.F8],
            I(0, CilOperation.LoadLocal, Local(0)), I(1, CilOperation.LoadLocal, Local(1)),
            I(2, branch, Target(5)),
            I(3, CilOperation.LoadFloat64, new CilOperand.ConstantF8(1)), I(4, CilOperation.Return),
            I(5, CilOperation.LoadFloat64, new CilOperand.ConstantF8(2)), I(6, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(compare, result.Instructions[4].Operation);
        // The value selected when the comparison is true: the taken arm's 2, or the fallthrough's 1.
        Assert.Equal(takenWhenTrue ? 2d : 1d, ((CilOperand.ConstantF8)result.Instructions[0].Operand).Value);
        _ = Validate(result);
    }

    [Theory]
    [InlineData(CliValueKind.I4, CilOperation.BranchIfTrue, 0)]
    [InlineData(CliValueKind.I8, CilOperation.BranchIfFalse, 2)]
    [InlineData(CliValueKind.NativeInt, CilOperation.BranchIfTrue, 3)]
    public void AWideTruthValueIsComparedWithZero(CliValueKind kind, CilOperation branch, int conditionInstructions)
    {
        var body = Body(CliValueKind.I4, [kind],
            I(0, CilOperation.LoadLocal, Local(0)),
            I(1, branch, Target(4)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(3, CilOperation.Return),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(5, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        // Two arms, the truth value, its comparison with zero, select, return.
        Assert.Equal(2 + 1 + conditionInstructions + 2, result.Instructions.Length);
        Assert.Equal(CilOperation.Select, result.Instructions[^2].Operation);
        _ = Validate(result);
    }

    // A conditional the select of another makes pure becomes a select in the next round.
    [Fact]
    public void NestedConditionalsBecomeNestedSelects()
    {
        var body = Body(CliValueKind.I4, [],
            I(0, CilOperation.LoadArgument, Arg(0)),
            I(1, CilOperation.BranchIfTrue, Target(8)),
            I(2, CilOperation.LoadArgument, Arg(1)),
            I(3, CilOperation.BranchIfTrue, Target(6)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(5, CilOperation.Branch, Target(7)),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(7, CilOperation.Return),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(9, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.DoesNotContain(result.Instructions, instruction => instruction.Operand is CilOperand.BranchTarget);
        Assert.Equal(2, result.Instructions.Count(instruction => instruction.Operation == CilOperation.Select));
        _ = Validate(result);
    }

    public static TheoryData<string, CilMethodBody> Unconvertible => new()
    {
        // An arm that divides, reads memory or calls could trap or do what its branch did not.
        { "divide", ValueReturns(I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.LoadArgument, Arg(1)), I(5, CilOperation.Divide)) },
        { "checked", ValueReturns(I(3, CilOperation.LoadArgument, Arg(0)),
            I(4, CilOperation.ConvertNumeric, new CilOperand.NumericConversion(8, false, true, false, false))) },
        // An arm longer than a mispredicted branch costs.
        { "long", ValueReturns([.. Enumerable.Range(3, 7).Select(offset => offset == 3
            ? I(offset, CilOperation.LoadArgument, Arg(0))
            : I(offset, offset % 2 == 0 ? CilOperation.LoadArgument : CilOperation.Add, offset % 2 == 0 ? Arg(1) : null))]) },
        // Returns of different kinds, an int32 a native int method returns, have no select.
        { "kinds", Body(CliValueKind.NativeInt, [CliValueKind.NativeInt],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfTrue, Target(4)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(3, CilOperation.Return),
            I(4, CilOperation.LoadLocal, Local(0)), I(5, CilOperation.Return)) },
        // Arms that store to different locals do two things.
        { "locals", Body(CliValueKind.I4, [CliValueKind.I4, CliValueKind.I4],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfTrue, Target(5)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(3, CilOperation.StoreLocal, Local(0)),
            I(4, CilOperation.Branch, Target(7)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(6, CilOperation.StoreLocal, Local(1)),
            I(7, CilOperation.LoadLocal, Local(0)), I(8, CilOperation.Return)) },
        // References keep their branches.
        { "references", Body(CliValueKind.ManagedReference, [CliValueKind.ManagedReference],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfTrue, Target(4)),
            I(2, CilOperation.LoadNull), I(3, CilOperation.Return),
            I(4, CilOperation.LoadLocal, Local(0)), I(5, CilOperation.Return)) },
        // A jump into the fallthrough arm.
        { "entered", Body(CliValueKind.I4, [],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfTrue, Target(6)),
            I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.BranchIfTrue, Target(4)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(5, CilOperation.Branch, Target(7)),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(7, CilOperation.Return)) },
    };

    [Theory]
    [MemberData(nameof(Unconvertible))]
    public void BranchesItCannotSelectAreKept(string shape, CilMethodBody body)
    {
        Assert.NotEmpty(shape);
        Assert.Same(body, Rewriter().Rewrite(body));
    }

    // A conversion renumbers instructions, which the offset-keyed proofs could not follow.
    [Fact]
    public void BodiesWithOffsetProofsAreKept()
    {
        var plain = ValueReturns(I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)));
        Assert.NotSame(plain, Rewriter().Rewrite(plain));
        var proven = plain with { ArrayBoundsProof = new([], [], [], []) };
        Assert.Same(proven, Rewriter().Rewrite(proven));
        var checkedBody = plain with { IntegerCheckProof = new([], [], []) };
        Assert.Same(checkedBody, Rewriter().Rewrite(checkedBody));
    }

    // A filtered try ahead of the conditional keeps its try, filter and handler as the instructions renumber.
    [Fact]
    public void ExceptionRegionsKeepTheirInstructions()
    {
        var body = Body(CliValueKind.I4, [],
            I(0, CilOperation.Nop), I(1, CilOperation.Leave, Target(7)),
            I(2, CilOperation.Pop), I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(4, CilOperation.EndFilter),
            I(5, CilOperation.Pop), I(6, CilOperation.Leave, Target(7)),
            I(7, CilOperation.LoadArgument, Arg(0)), I(8, CilOperation.LoadArgument, Arg(1)),
            I(9, CilOperation.BranchIfGreaterThanSigned, Target(12)),
            I(10, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(11, CilOperation.Return),
            I(12, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(13, CilOperation.Return)) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Filter, 0, 2, 5, 2, null, 2)],
        };

        var result = Rewriter().Rewrite(body);

        Assert.Equal(body.ExceptionRegions, result.ExceptionRegions);
        Assert.Equal(CilOperation.Select, result.Instructions[^2].Operation);
        _ = Validate(result);
    }

    // if (a < 0) a = 0; on a parameter stores a select to the argument, as it does to a local.
    [Fact]
    public void ATriangleStoringToAnArgumentStoresOneSelect()
    {
        var body = Body(CliValueKind.I4, [],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(5)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(4, CilOperation.StoreArgument, Arg(0)),
            I(5, CilOperation.LoadArgument, Arg(0)), I(6, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadInt32, CilOperation.LoadArgument,
                CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.CompareLessThanSigned,
                CilOperation.Select, CilOperation.StoreArgument, CilOperation.LoadArgument, CilOperation.Return,
            ],
            Operations(result));
        Assert.Equal(0, ((CilOperand.Index)result.Instructions[6].Operand).Value);
        _ = Validate(result);
    }

    // An int32 stored to a native int local widens there: the arm's value is not the slot's kind, so the triangle stays.
    [Fact]
    public void ATriangleStoringToAWiderSlotIsKept()
    {
        var body = Body(CliValueKind.I4, [CliValueKind.NativeInt],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(5)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(4, CilOperation.StoreLocal, Local(0)),
            I(5, CilOperation.LoadArgument, Arg(0)), I(6, CilOperation.Return));
        _ = Validate(body);

        Assert.Same(body, Rewriter().Rewrite(body));
    }

    // for (i = 0; i < b; i++) { <head> if (a > 1) sum += a; } return sum; where sum is local 0, i local 1, and head
    // runs at the top of each iteration.
    private static CilMethodBody Accumulation(params CilInstruction[] head)
    {
        var top = 4 + head.Length;
        return Body(CliValueKind.I4, [CliValueKind.I4, CliValueKind.I4],
            [
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(1, CilOperation.StoreLocal, Local(0)),
                I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), I(3, CilOperation.StoreLocal, Local(1)),
                .. head,
                I(top, CilOperation.LoadArgument, Arg(0)), I(top + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(top + 2, CilOperation.BranchIfLessThanOrEqualSigned, Target(top + 7)),
                I(top + 3, CilOperation.LoadLocal, Local(0)), I(top + 4, CilOperation.LoadArgument, Arg(0)),
                I(top + 5, CilOperation.Add), I(top + 6, CilOperation.StoreLocal, Local(0)),
                I(top + 7, CilOperation.LoadLocal, Local(1)), I(top + 8, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(top + 9, CilOperation.Add), I(top + 10, CilOperation.StoreLocal, Local(1)),
                I(top + 11, CilOperation.LoadLocal, Local(1)), I(top + 12, CilOperation.LoadArgument, Arg(1)),
                I(top + 13, CilOperation.BranchIfLessThanSigned, Target(4)),
                I(top + 14, CilOperation.LoadLocal, Local(0)), I(top + 15, CilOperation.Return),
            ]);
    }

    // A select on an accumulator the loop carries would put the element test on the loop's dependency chain.
    [Fact]
    public void AConditionalUpdateOfAValueTheLoopCarriesKeepsItsBranch()
    {
        var untouched = Accumulation();
        // sum ^= 1 reads the carried value before the loop writes it.
        var readFirst = Accumulation(
            I(4, CilOperation.LoadLocal, Local(0)), I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(6, CilOperation.BitwiseXor), I(7, CilOperation.StoreLocal, Local(0)));
        _ = Validate(untouched);
        _ = Validate(readFirst);

        Assert.Same(untouched, Rewriter().Rewrite(untouched));
        Assert.Same(readFirst, Rewriter().Rewrite(readFirst));
    }

    // sum = b at the top of each iteration: the loop writes the slot first, so its value is the iteration's own.
    [Fact]
    public void AConditionalUpdateOfAnIterationsOwnValueSelects()
    {
        var body = Accumulation(I(4, CilOperation.LoadArgument, Arg(1)), I(5, CilOperation.StoreLocal, Local(0)));

        var result = Rewriter().Rewrite(body);

        Assert.Contains(CilOperation.Select, Operations(result));
        Assert.DoesNotContain(CilOperation.BranchIfLessThanOrEqualSigned, Operations(result));
        _ = Validate(result);
    }

    // if (a > b) return <arm>; return 0; where the fallthrough arm is `arm`.
    private static CilMethodBody ValueReturns(params CilInstruction[] arm)
    {
        var target = 3 + arm.Length + 1;
        return Body(CliValueKind.I4, [],
            [
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadArgument, Arg(1)),
                I(2, CilOperation.BranchIfGreaterThanSigned, Target(target)),
                .. arm,
                I(target - 1, CilOperation.Return),
                I(target, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(target + 1, CilOperation.Return),
            ]);
    }

    private static ConditionalSelectRewriter Rewriter()
    {
        var program = new Program();
        return new(new ControlFlowGraphBuilderFactory().Create(),
            new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program));
    }

    private static ValidatedControlFlowGraph Validate(CilMethodBody body)
    {
        var program = new Program();
        return new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program)
            .Validate(new ControlFlowGraphBuilderFactory().Create().Build(body));
    }

    // A static method of two int32 arguments.
    private static CilMethodBody Body(CliValueKind result, ImmutableArray<CliValueKind> locals, params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Fixture, 0x06000001), new(Fixture, 0x02000002), "Run", true,
                new MethodSignatureModel(result, [CliValueKind.I4, CliValueKind.I4]), 1), 3, locals, [.. instructions]);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilOperand.Index Arg(int index) => new(index);

    private static CilOperand.Index Local(int index) => new(index);

    private static CilOperand.BranchTarget Target(int offset) => new(offset);

    private static CilOperation[] Operations(CilMethodBody body) => [.. body.Instructions.Select(instruction => instruction.Operation)];

    private static int[] Arguments(CilMethodBody body) => [.. body.Instructions
        .Where(instruction => instruction.Operation == CilOperation.LoadArgument)
        .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)];

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
