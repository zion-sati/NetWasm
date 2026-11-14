using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ConstantBranchRewriterTests
{
    [Fact]
    public void UnknownAssignmentInvalidatesAnEarlierLocalConstant()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.Return));

        Assert.Same(body, Rule(body).Rewrite(body));
    }

    // The null an unwritten reference-typed static field folds to decides the branches on it.
    [Theory]
    [InlineData(CilOperation.BranchIfTrue, false)]
    [InlineData(CilOperation.BranchIfFalse, true)]
    public void FoldsBranchesOnANullReference(CilOperation branch, bool taken)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, branch, new CilOperand.BranchTarget(3)),
            Instruction(2, CilOperation.Return),
            Instruction(3, CilOperation.Return));

        var rewritten = Rule(body).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(taken ? CilOperation.Branch : CilOperation.Nop, rewritten.Instructions[1].Operation);
    }

    // `field ?? fallback`: the dup the branch reads goes, and the null it copied stays for the fallthrough's pop.
    [Fact]
    public void FoldsTheNullCoalescingBranchOnADuplicatedNull()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.Duplicate),
            Instruction(2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            Instruction(3, CilOperation.Pop),
            Instruction(4, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(5, CilOperation.Return));

        var rewritten = Rule(body).Rewrite(body);

        Assert.Equal(CilOperation.LoadNull, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[2].Operation);
        Assert.Equal(CilOperation.Pop, rewritten.Instructions[3].Operation);
    }

    // `fixed (byte* p = array)` with a null array: the dup the local takes stays, so when the branch folds the
    // null it copied away, the dup pushes the null itself.
    [Fact]
    public void ADupStillReadAfterItsFoldedSourcePushesTheNullItself()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.Duplicate),
            Instruction(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
            Instruction(4, CilOperation.Return),
            Instruction(5, CilOperation.Return));

        var rewritten = Rule(body).Rewrite(body);

        Assert.Equal(
            [CilOperation.Nop, CilOperation.LoadNull, CilOperation.StoreLocal, CilOperation.Branch],
            rewritten.Instructions.Take(4).Select(static instruction => instruction.Operation));
    }

    // A dup folded before the null it copied stays folded.
    [Fact]
    public void ADupFoldedBeforeItsSourceStaysFolded()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.Duplicate),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, CilOperation.CompareEqual),
            Instruction(4, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.LoadNull),
            Instruction(6, CilOperation.CompareEqual),
            Instruction(7, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(9)),
            Instruction(8, CilOperation.Return),
            Instruction(9, CilOperation.Return));

        var rewritten = Rule(body).Rewrite(body);

        Assert.Equal(
            [CilOperation.Nop, CilOperation.Nop, CilOperation.Nop, CilOperation.LoadInt32, CilOperation.StoreLocal,
                CilOperation.Nop, CilOperation.Nop, CilOperation.Branch],
            rewritten.Instructions.Take(8).Select(static instruction => instruction.Operation));
    }

    // `field != null` compiles to cgt.un against null.
    [Fact]
    public void FoldsAnUnsignedComparisonOfTwoNulls()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadNull),
            Instruction(2, CilOperation.CompareGreaterThanUnsigned),
            Instruction(3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(5)),
            Instruction(4, CilOperation.Return),
            Instruction(5, CilOperation.Return));

        var rewritten = Rule(body).Rewrite(body);

        Assert.Equal(CilOperation.Branch, rewritten.Instructions[3].Operation);
        Assert.All(rewritten.Instructions.Take(3), instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
    }

    // A null stored to a local decides the branches on the local's loads, as an earlier pass leaves a static field read
    // once and kept in a local; the store stays, and a load is never turned into an int32 constant.
    [Fact]
    public void FoldsBranchesOnANullStoredToALocal()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(6)),
            Instruction(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(5, CilOperation.Return),
            Instruction(6, CilOperation.Return));

        var rewritten = Rule(body).Rewrite(body);

        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[2].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[3].Operation);
        Assert.Equal(CilOperation.LoadLocal, rewritten.Instructions[4].Operation);
    }

    // A null never compares with an integer constant, which verifiable IL does not do.
    [Fact]
    public void DoesNotCompareANullWithAnInteger()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadNull),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            Instruction(2, CilOperation.CompareEqual),
            Instruction(3, CilOperation.Return));

        Assert.Same(body, Rule(body).Rewrite(body));
    }

    [Fact]
    public void RejectsMissingGraphBuilderAndBody()
    {
        Assert.Throws<ArgumentNullException>(() => new ConstantBranchRewriter(null!));
        var body = Body(Instruction(0, CilOperation.Return));
        Assert.Throws<ArgumentNullException>(() => Rule(body).Rewrite(null!));
    }

    [Theory]
    [InlineData(0, CilOperation.BranchIfTrue, false)]
    [InlineData(1, CilOperation.BranchIfTrue, true)]
    [InlineData(-1, CilOperation.BranchIfTrue, true)]
    [InlineData(0, CilOperation.BranchIfFalse, true)]
    [InlineData(1, CilOperation.BranchIfFalse, false)]
    public void FoldsBlockLocalBooleanCopies(int value, CilOperation branch, bool taken)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, CilOperation.Nop),
            Instruction(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(4, branch, new CilOperand.BranchTarget(6)),
            Instruction(5, CilOperation.Return),
            Instruction(6, CilOperation.Return));
        var rule = Rule(body);

        var rewritten = rule.Rewrite(body);

        Assert.Equal(CilOperation.StoreLocal, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[3].Operation);
        Assert.Equal(taken ? CilOperation.Branch : CilOperation.Nop, rewritten.Instructions[4].Operation);
        Assert.Equal(body.Instructions[4].SourceOffset, rewritten.Instructions[4].SourceOffset);
    }

    [Theory]
    [InlineData(CilOperation.Add, 7, 5, 12)]
    [InlineData(CilOperation.Subtract, int.MinValue, 1, int.MaxValue)]
    [InlineData(CilOperation.Multiply, 16, -2, -32)]
    [InlineData(CilOperation.Divide, -7, 2, -3)]
    [InlineData(CilOperation.Remainder, -7, 2, -1)]
    [InlineData(CilOperation.DivideUnsigned, -2, 2, int.MaxValue)]
    [InlineData(CilOperation.RemainderUnsigned, -1, 10, 5)]
    [InlineData(CilOperation.BitwiseAnd, 12, 10, 8)]
    [InlineData(CilOperation.BitwiseOr, 12, 10, 14)]
    [InlineData(CilOperation.BitwiseXor, 12, 10, 6)]
    [InlineData(CilOperation.ShiftLeft, 1, 33, 2)]
    [InlineData(CilOperation.ShiftRightSigned, -64, 34, -16)]
    [InlineData(CilOperation.ShiftRightUnsigned, -64, 28, 15)]
    [InlineData(CilOperation.CompareGreaterThanSigned, -1, 0, 0)]
    [InlineData(CilOperation.CompareGreaterThanUnsigned, -1, 0, 1)]
    [InlineData(CilOperation.CompareLessThanSigned, -1, 0, 1)]
    [InlineData(CilOperation.CompareLessThanUnsigned, -1, 0, 0)]
    public void FoldsBinaryOperationsOnConstants(CilOperation operation, int left, int right, int expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(right)),
            Instruction(2, operation),
            Instruction(3, CilOperation.Return));

        var rewritten = new ConstantBranchRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[2].Operation);
        Assert.Equal(new CilOperand.ConstantI4(expected), rewritten.Instructions[2].Operand);
    }

    // Each of these throws at run time, so it stays.
    [Theory]
    [InlineData(CilOperation.Divide, 1, 0)]
    [InlineData(CilOperation.Remainder, 1, 0)]
    [InlineData(CilOperation.DivideUnsigned, 1, 0)]
    [InlineData(CilOperation.RemainderUnsigned, 1, 0)]
    [InlineData(CilOperation.Divide, int.MinValue, -1)]
    [InlineData(CilOperation.Remainder, int.MinValue, -1)]
    public void KeepsOperationsThatThrow(CilOperation operation, int left, int right)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(right)),
            Instruction(2, operation),
            Instruction(3, CilOperation.Return));

        Assert.Same(body, new ConstantBranchRewriter(Graph(body)).Rewrite(body));
    }

    [Theory]
    [InlineData(CilOperation.Negate, 5, -5)]
    [InlineData(CilOperation.Negate, int.MinValue, int.MinValue)]
    [InlineData(CilOperation.OnesComplement, 5, -6)]
    public void FoldsUnaryOperationsOnConstants(CilOperation operation, int value, int expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(value)),
            Instruction(1, operation),
            Instruction(2, CilOperation.Return));

        var rewritten = new ConstantBranchRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(new CilOperand.ConstantI4(expected), rewritten.Instructions[1].Operand);
    }

    [Theory]
    [InlineData(CilOperation.BranchIfEqual, 4, 4, true)]
    [InlineData(CilOperation.BranchIfNotEqual, 4, 4, false)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned, -1, 2, false)]
    [InlineData(CilOperation.BranchIfGreaterThanUnsigned, -1, 2, true)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, 2, 2, true)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned, 1, -1, false)]
    [InlineData(CilOperation.BranchIfLessThanSigned, -1, 2, true)]
    [InlineData(CilOperation.BranchIfLessThanUnsigned, -1, 2, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, 3, 2, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned, 2, -1, true)]
    public void FoldsTwoOperandBranchesOnConstants(CilOperation branch, int left, int right, bool taken)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(right)),
            Instruction(2, branch, new CilOperand.BranchTarget(4)),
            Instruction(3, CilOperation.Return),
            Instruction(4, CilOperation.Return));

        var rewritten = new ConstantBranchRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(taken ? CilOperation.Branch : CilOperation.Nop, rewritten.Instructions[2].Operation);
    }

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(2, 3, 0)]
    public void FoldsConstantEquality(int left, int right, int expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(left)),
            Instruction(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(right)),
            Instruction(2, CilOperation.CompareEqual),
            Instruction(3, CilOperation.Return));

        var rewritten = new ConstantBranchRewriter(Graph(body)).Rewrite(body);

        Assert.Equal(CilOperation.Nop, rewritten.Instructions[0].Operation);
        Assert.Equal(CilOperation.Nop, rewritten.Instructions[1].Operation);
        Assert.Equal(new CilOperand.ConstantI4(expected), rewritten.Instructions[2].Operand);
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.LoadLocalAddress)]
    [InlineData(CilOperation.StoreObject)]
    public void ForgetsLocalFactsAcrossPossibleEffects(CilOperation effect)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            Instruction(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(2, effect),
            Instruction(3, CilOperation.LoadLocal, new CilOperand.Index(0)),
            Instruction(4, CilOperation.Return));

        Assert.Same(body, new ConstantBranchRewriter(Graph(body)).Rewrite(body));
    }

    internal static IMethodRewriteRule Rule(CilMethodBody body) => new ConstantBranchRewriter(Graph(body));
}
