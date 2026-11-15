// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class ClosedTypeComparisonRewriterTests
{
    [Fact]
    public void FoldsDirectReferenceEqualityWithoutCallingAPredicate()
    {
        var body = ComparisonBody(CilOperation.CompareEqual, null);
        var result = new ClosedTypeComparisonRewriter(Graph(body), new MethodFacts(),
            new Comparison(null), new TypeComparison(true)).Rewrite(body);

        Assert.Equal(new CilOperand.ConstantI4(1), result.Instructions[4].Operand);
        Assert.All(result.Instructions.Take(4), instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
    }

    [Fact]
    public void RejectsMissingDependenciesAndBody()
    {
        var body = ComparisonBody(CilOperation.CompareEqual, null);
        var graph = Graph(body);
        var calls = new MethodFacts();
        var comparison = new Comparison(null);
        var types = new TypeComparison(null);
        Assert.Throws<ArgumentNullException>(() => new ClosedTypeComparisonRewriter(null!, calls, comparison, types));
        Assert.Throws<ArgumentNullException>(() => new ClosedTypeComparisonRewriter(graph, null!, comparison, types));
        Assert.Throws<ArgumentNullException>(() => new ClosedTypeComparisonRewriter(graph, calls, null!, types));
        Assert.Throws<ArgumentNullException>(() => new ClosedTypeComparisonRewriter(graph, calls, comparison, null!));
        Assert.Throws<ArgumentNullException>(() => new ClosedTypeComparisonRewriter(graph, calls, comparison, types).Rewrite(null!));
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.CallVirtual)]
    public void DoesNotFoldUnresolvedOrNonStaticComparisonOperations(CilOperation operation)
    {
        var body = ComparisonBody(operation, new CilOperand.None());
        Assert.Same(body, new ClosedTypeComparisonRewriter(Graph(body), new MethodFacts(),
            new Comparison(ReferenceComparison.Equal), new TypeComparison(true)).Rewrite(body));
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    public void FoldsBodyProvenPredicates(bool equal, bool inverted, int expected)
    {
        var body = ComparisonBody(CilOperation.Call, new CilOperand.MethodInstance(Method()));
        var rule = new ClosedTypeComparisonRewriter(Graph(body), new MethodFacts(),
            new Comparison(inverted ? ReferenceComparison.NotEqual : ReferenceComparison.Equal), new TypeComparison(equal));

        var rewritten = rule.Rewrite(body);

        Assert.All(rewritten.Instructions.Take(4), instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
        Assert.Equal(CilOperation.LoadInt32, rewritten.Instructions[4].Operation);
        Assert.Equal(new CilOperand.ConstantI4(expected), rewritten.Instructions[4].Operand);
    }

    [Fact]
    public void LeavesUnprovenPredicatesUnchanged()
    {
        var body = ComparisonBody(CilOperation.Call, new CilOperand.MethodInstance(Method()));
        var rule = new ClosedTypeComparisonRewriter(Graph(body), new MethodFacts(), new Comparison(null), new TypeComparison(true));

        Assert.Same(body, rule.Rewrite(body));
    }

    [Theory]
    [InlineData(CilOperation.BranchIfEqual, CilOperation.BranchIfTrue)]
    [InlineData(CilOperation.BranchIfNotEqual, CilOperation.BranchIfFalse)]
    public void PreservesBranchDestinations(CilOperation original, CilOperation folded)
    {
        var body = ComparisonBody(original, new CilOperand.BranchTarget(5));
        var rule = new ClosedTypeComparisonRewriter(Graph(body), new MethodFacts(), new Comparison(null), new TypeComparison(false));

        var rewritten = rule.Rewrite(body);

        Assert.Equal(new CilOperand.ConstantI4(0), rewritten.Instructions[3].Operand);
        Assert.Equal(folded, rewritten.Instructions[4].Operation);
        Assert.Equal(body.Instructions[4].Operand, rewritten.Instructions[4].Operand);
    }

    [Fact]
    public void DoesNotConsumeInputsAcrossBlockEntries()
    {
        var body = ComparisonBody(CilOperation.CompareEqual, null);
        body = body with { Instructions = body.Instructions.Add(Instruction(6, CilOperation.Branch, new CilOperand.BranchTarget(2))) };
        var rule = new ClosedTypeComparisonRewriter(Graph(body), new MethodFacts(), new Comparison(null), new TypeComparison(true));

        Assert.Same(body, rule.Rewrite(body));
    }

    private static CilMethodBody ComparisonBody(CilOperation operation, CilOperand? operand) => Body(
        Instruction(0, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Owner)),
        Instruction(1, CilOperation.MaterializeType),
        Instruction(2, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(Owner)),
        Instruction(3, CilOperation.MaterializeType),
        Instruction(4, operation, operand),
        Instruction(5, CilOperation.Return));
}
