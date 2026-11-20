// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// An arm that stores one value and then checks it, throwing when the check fails, as upstream's Math.Abs is written,
// gives its check to the code after it, so if-conversion can select the arm's store; anything that could not run
// whichever way the outer branch went keeps its shape.
public sealed class ThrowCheckHoisterTests
{
    private static readonly AssemblyIdentity Fixture = new("fixture");

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        Assert.Throws<ArgumentNullException>(() => new ThrowCheckHoister(null!, Validator()));
        Assert.Throws<ArgumentNullException>(() => new ThrowCheckHoister(new ControlFlowGraphBuilderFactory().Create(), null!));
        Assert.Throws<ArgumentNullException>(() => Hoister().Rewrite(null!));
    }

    // if (a < 0) { a = -a; if (a < 0) throw; } return a;: the check repeats the outer branch, so the outer branch skips
    // to it, and if-conversion leaves a select of a and one branch, to the throw.
    [Fact]
    public void AbsoluteValueSkipsToItsCheckAndBecomesASelect()
    {
        var body = Abs(CilOperation.StoreArgument, CilOperation.LoadArgument);

        var hoisted = Hoister().Rewrite(body);

        Assert.Equal(Operations(body), Operations(hoisted));
        Assert.Equal(8, ((CilOperand.BranchTarget)hoisted.Instructions[4].Operand).Offset);
        Assert.Equal(13, ((CilOperand.BranchTarget)hoisted.Instructions[10].Operand).Offset);
        Assert.Empty(hoisted.Locals);
        _ = Validate(hoisted);

        var selected = SelectRewriter().Rewrite(hoisted);

        Assert.Single(selected.Instructions, instruction => instruction.Operation == CilOperation.Select);
        Assert.Single(selected.Instructions, BranchShapes.IsForwardConditionalBranch);
        _ = Validate(selected);
    }

    // if (a < 0) { a = -a * 3; if (a > 100) throw; }: a check of another condition runs after the arm on both paths,
    // and fails only where the arm ran.
    [Fact]
    public void ACheckOfAnotherConditionTestsBothConditions()
    {
        var body = Body([],
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
            I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.Negate), I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(6, CilOperation.Multiply), I(7, CilOperation.StoreArgument, Arg(0)),
            I(8, CilOperation.LoadArgument, Arg(0)), I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(100)),
            I(10, CilOperation.BranchIfLessThanOrEqualSigned, Target(13)),
            I(11, CilOperation.LoadNull), I(12, CilOperation.Throw),
            I(13, CilOperation.LoadArgument, Arg(0)), I(14, CilOperation.Return));

        var hoisted = Hoister().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.CompareLessThanSigned,
                CilOperation.LoadInt32, CilOperation.CompareEqual, CilOperation.StoreLocal, CilOperation.LoadLocal,
                CilOperation.BranchIfTrue,
                CilOperation.LoadArgument, CilOperation.Negate, CilOperation.LoadInt32, CilOperation.Multiply, CilOperation.StoreArgument,
                CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.CompareGreaterThanSigned,
                CilOperation.LoadInt32, CilOperation.CompareEqual, CilOperation.LoadLocal, CilOperation.BitwiseOr,
                CilOperation.BranchIfTrue,
                CilOperation.LoadNull, CilOperation.Throw, CilOperation.LoadArgument, CilOperation.Return,
            ],
            Operations(hoisted));
        // The first branch skips the store to the check; the second skips the throw to the join.
        Assert.Equal(13, ((CilOperand.BranchTarget)hoisted.Instructions[7].Operand).Offset);
        Assert.Equal(23, ((CilOperand.BranchTarget)hoisted.Instructions[20].Operand).Offset);
        Assert.Equal([CliValueKind.I4], hoisted.Locals);
        _ = Validate(hoisted);

        var selected = SelectRewriter().Rewrite(hoisted);

        Assert.Single(selected.Instructions, instruction => instruction.Operation == CilOperation.Select);
        Assert.Single(selected.Instructions, BranchShapes.IsForwardConditionalBranch);
        _ = Validate(selected);
    }

    // A check that only looks like the outer branch tests both conditions: another constant, another comparison, outer
    // operands that code elsewhere branches into, or fewer instructions before the outer branch than the check has.
    [Theory]
    [InlineData("other constant")]
    [InlineData("other comparison")]
    [InlineData("entered operands")]
    [InlineData("short prefix")]
    public void ALookalikeCheckTestsBothConditions(string shape)
    {
        var body = shape switch
        {
            "other constant" => Abs(CilOperation.StoreArgument, CilOperation.LoadArgument) is var abs
                ? abs with { Instructions = abs.Instructions.SetItem(9, I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(-1))) }
                : null!,
            "other comparison" => Abs(CilOperation.StoreArgument, CilOperation.LoadArgument) is var abs
                ? abs with { Instructions = abs.Instructions.SetItem(10, I(10, CilOperation.BranchIfGreaterThanSigned, Target(13))) }
                : null!,
            // The outer branch compares an argument from one path and the other argument from another.
            "entered operands" => Body([],
                I(0, CilOperation.LoadArgument, Arg(1)), I(1, CilOperation.BranchIfFalse, Target(4)),
                I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.Branch, Target(5)),
                I(4, CilOperation.LoadArgument, Arg(0)),
                I(5, CilOperation.LoadInt32, Zero), I(6, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(15)),
                I(7, CilOperation.LoadArgument, Arg(0)), I(8, CilOperation.Negate), I(9, CilOperation.StoreArgument, Arg(0)),
                I(10, CilOperation.LoadArgument, Arg(0)), I(11, CilOperation.LoadInt32, Zero),
                I(12, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(15)),
                I(13, CilOperation.LoadNull), I(14, CilOperation.Throw),
                I(15, CilOperation.LoadArgument, Arg(0)), I(16, CilOperation.Return)),
            _ => Body([],
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfFalse, Target(9)),
                I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.StoreArgument, Arg(0)),
                I(4, CilOperation.LoadArgument, Arg(0)), I(5, CilOperation.LoadInt32, Zero),
                I(6, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(9)),
                I(7, CilOperation.LoadNull), I(8, CilOperation.Throw),
                I(9, CilOperation.LoadArgument, Arg(0)), I(10, CilOperation.Return)),
        };

        var hoisted = Hoister().Rewrite(body);

        Assert.Equal(body.Locals.Length + 1, hoisted.Locals.Length);
        Assert.Contains(hoisted.Instructions, instruction => instruction.Operation == CilOperation.BitwiseOr);
        _ = Validate(hoisted);
    }

    // The same arm storing to a local.
    [Fact]
    public void ALocalsArmIsHoistedAsAnArgumentsIs()
    {
        var selected = SelectRewriter().Rewrite(Hoister().Rewrite(Abs(CilOperation.StoreLocal, CilOperation.LoadLocal)));

        Assert.Single(selected.Instructions, instruction => instruction.Operation == CilOperation.Select);
        Assert.Single(selected.Instructions, BranchShapes.IsForwardConditionalBranch);
        _ = Validate(selected);
    }

    // What keeps the shape: a failure that does not throw, a check that skips elsewhere, an arm that calls, two stores,
    // a check that divides, which may trap, a value left under the outer branch's operands, and an arm entered from outside.
    [Theory]
    [InlineData("no throw")]
    [InlineData("other target")]
    [InlineData("arm calls")]
    [InlineData("two stores")]
    [InlineData("check divides")]
    [InlineData("value below")]
    [InlineData("entered")]
    [InlineData("one branch")]
    [InlineData("failure returns")]
    [InlineData("failure branches")]
    public void KeepsWhatCannotRunEitherWay(string shape)
    {
        var body = shape switch
        {
            "no throw" => Body([],
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(11)),
                I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.Negate), I(5, CilOperation.StoreArgument, Arg(0)),
                I(6, CilOperation.LoadArgument, Arg(0)), I(7, CilOperation.LoadInt32, Zero), I(8, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(11)),
                I(9, CilOperation.LoadInt32, Zero), I(10, CilOperation.StoreArgument, Arg(1)),
                I(11, CilOperation.LoadArgument, Arg(0)), I(12, CilOperation.Return)),
            "other target" => Body([],
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(11)),
                I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.Negate), I(5, CilOperation.StoreArgument, Arg(0)),
                I(6, CilOperation.LoadArgument, Arg(0)), I(7, CilOperation.LoadInt32, Zero), I(8, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(9, CilOperation.LoadNull), I(10, CilOperation.Throw),
                I(11, CilOperation.LoadArgument, Arg(0)), I(12, CilOperation.Return),
                I(13, CilOperation.LoadInt32, Zero), I(14, CilOperation.Return)),
            "arm calls" => Abs(CilOperation.StoreArgument, CilOperation.LoadArgument) is var abs
                ? abs with { Instructions = abs.Instructions.SetItem(4, I(4, CilOperation.Call, new CilOperand.None())) }
                : null!,
            "two stores" => Body([],
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(12)),
                I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.StoreArgument, Arg(1)),
                I(5, CilOperation.LoadArgument, Arg(0)), I(6, CilOperation.StoreArgument, Arg(0)),
                I(7, CilOperation.LoadArgument, Arg(0)), I(8, CilOperation.LoadInt32, Zero), I(9, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(12)),
                I(10, CilOperation.LoadNull), I(11, CilOperation.Throw),
                I(12, CilOperation.LoadArgument, Arg(0)), I(13, CilOperation.Return)),
            "check divides" => Body([],
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.Negate), I(5, CilOperation.StoreArgument, Arg(0)),
                I(6, CilOperation.LoadArgument, Arg(0)), I(7, CilOperation.LoadArgument, Arg(1)), I(8, CilOperation.Divide),
                I(9, CilOperation.LoadInt32, Zero), I(10, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(11, CilOperation.LoadNull), I(12, CilOperation.Throw),
                I(13, CilOperation.LoadArgument, Arg(0)), I(14, CilOperation.Return)),
            "value below" => Body([],
                I(0, CilOperation.LoadInt32, Zero),
                I(1, CilOperation.LoadArgument, Arg(0)), I(2, CilOperation.LoadInt32, Zero), I(3, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(12)),
                I(4, CilOperation.LoadArgument, Arg(0)), I(5, CilOperation.Negate), I(6, CilOperation.StoreArgument, Arg(0)),
                I(7, CilOperation.LoadArgument, Arg(0)), I(8, CilOperation.LoadInt32, Zero), I(9, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(12)),
                I(10, CilOperation.LoadNull), I(11, CilOperation.Throw),
                I(12, CilOperation.Pop), I(13, CilOperation.LoadArgument, Arg(0)), I(14, CilOperation.Return)),
            "entered" => Body([],
                I(0, CilOperation.LoadArgument, Arg(1)), I(1, CilOperation.BranchIfTrue, Target(8)),
                I(2, CilOperation.LoadArgument, Arg(0)), I(3, CilOperation.LoadInt32, Zero), I(4, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(5, CilOperation.LoadArgument, Arg(0)), I(6, CilOperation.Negate), I(7, CilOperation.StoreArgument, Arg(0)),
                I(8, CilOperation.LoadArgument, Arg(0)), I(9, CilOperation.LoadInt32, Zero), I(10, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(11, CilOperation.LoadNull), I(12, CilOperation.Throw),
                I(13, CilOperation.LoadArgument, Arg(0)), I(14, CilOperation.Return)),
            // The failure does not run straight into its throw: it returns or branches first.
            "failure returns" => Failure(I(11, CilOperation.LoadArgument, Arg(0)), I(12, CilOperation.Return)),
            "failure branches" => Failure(I(11, CilOperation.LoadArgument, Arg(1)), I(12, CilOperation.BranchIfFalse, Target(13))),
            _ => Body([],
                I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(5)),
                I(3, CilOperation.LoadNull), I(4, CilOperation.Throw),
                I(5, CilOperation.LoadArgument, Arg(0)), I(6, CilOperation.Return)),
        };

        Assert.Same(body, Hoister().Rewrite(body));
    }

    // Proofs are keyed by offsets, which the rewrite renumbers.
    [Fact]
    public void LeavesABodyWithProofs()
    {
        var body = Abs(CilOperation.StoreArgument, CilOperation.LoadArgument) with
        {
            IntegerCheckProof = new([], [], []),
        };

        Assert.Same(body, Hoister().Rewrite(body));
    }

    // if (v < 0) { v = -v; if (v < 0) throw null; } return v; for an argument or a local copy of it.
    private static CilMethodBody Abs(CilOperation store, CilOperation load)
    {
        var slot = store == CilOperation.StoreLocal ? Local(0) : Arg(0);
        CilInstruction[] copy = store == CilOperation.StoreLocal
            ? [I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.StoreLocal, Local(0))]
            : [I(0, CilOperation.Nop), I(1, CilOperation.Nop)];
        return Body(store == CilOperation.StoreLocal ? [CliValueKind.I4] : [],
            [
                .. copy,
                I(2, load, slot), I(3, CilOperation.LoadInt32, Zero), I(4, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(5, load, slot), I(6, CilOperation.Negate), I(7, store, slot),
                I(8, load, slot), I(9, CilOperation.LoadInt32, Zero), I(10, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(13)),
                I(11, CilOperation.LoadNull), I(12, CilOperation.Throw),
                I(13, load, slot), I(14, CilOperation.Return),
            ]);
    }

    // Abs with two instructions before its failure's ldnull; throw.
    private static CilMethodBody Failure(CilInstruction first, CilInstruction second) => Body([],
        I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadInt32, Zero), I(2, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(15)),
        I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.Negate), I(5, CilOperation.StoreArgument, Arg(0)),
        I(6, CilOperation.LoadArgument, Arg(0)), I(7, CilOperation.LoadInt32, Zero), I(8, CilOperation.BranchIfGreaterThanOrEqualSigned, Target(15)),
        I(9, CilOperation.Nop), I(10, CilOperation.Nop), first, second,
        I(13, CilOperation.LoadNull), I(14, CilOperation.Throw),
        I(15, CilOperation.LoadArgument, Arg(0)), I(16, CilOperation.Return));

    private static readonly CilOperand.ConstantI4 Zero = new(0);

    private static ITypedStackValidator Validator()
    {
        var program = new Program();
        return new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
    }

    private static ThrowCheckHoister Hoister() => new(new ControlFlowGraphBuilderFactory().Create(), Validator());

    private static ConditionalSelectRewriter SelectRewriter() => new(new ControlFlowGraphBuilderFactory().Create(), Validator());

    private static ValidatedControlFlowGraph Validate(CilMethodBody body) =>
        Validator().Validate(new ControlFlowGraphBuilderFactory().Create().Build(body));

    // A static method of two int32 arguments returning an int32.
    private static CilMethodBody Body(ImmutableArray<CliValueKind> locals, params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Fixture, 0x06000001), new(Fixture, 0x02000002), "Run", true,
                new MethodSignatureModel(CliValueKind.I4, [CliValueKind.I4, CliValueKind.I4]), 1), 3, locals, [.. instructions]);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilOperand.Index Arg(int index) => new(index);

    private static CilOperand.Index Local(int index) => new(index);

    private static CilOperand.BranchTarget Target(int offset) => new(offset);

    private static CilOperation[] Operations(CilMethodBody body) => [.. body.Instructions.Select(instruction => instruction.Operation)];

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
