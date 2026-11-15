// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

using static ConstantRewriteTestSupport;

public sealed class BoxedNullComparisonRewriterTests
{
    private static readonly CliTypeIdentity Integer = CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Theory]
    [InlineData(CilOperation.CompareEqual, 0)]
    [InlineData(CilOperation.CompareGreaterThanUnsigned, 1)]
    public void ReplacesUnusedBoxButPreservesValueEvaluation(CilOperation comparison, int expected)
    {
        var body = Body(
            Instruction(0, CilOperation.Call, new CilOperand.MethodInstance(Method())),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, comparison),
            Instruction(4, CilOperation.Return));
        var result = Rule(body).Rewrite(body);

        Assert.Equal(body.Instructions[0], result.Instructions[0]);
        Assert.Equal(CilOperation.Pop, result.Instructions[1].Operation);
        Assert.Equal(CilOperation.LoadInt32, result.Instructions[2].Operation);
        Assert.Equal(new CilOperand.ConstantI4(expected), result.Instructions[2].Operand);
        Assert.Equal(CilOperation.Nop, result.Instructions[3].Operation);
        Assert.Equal(body.Instructions.Select(i => i.Offset), result.Instructions.Select(i => i.Offset));
        Assert.Equal(body.Instructions.Select(i => i.SourceOffset), result.Instructions.Select(i => i.SourceOffset));
    }

    [Theory]
    [InlineData(CilOperation.BranchIfTrue, CilOperation.Branch)]
    [InlineData(CilOperation.BranchIfFalse, CilOperation.Nop)]
    public void FoldsBoxedValueConditionalBranches(CilOperation branch, CilOperation expected)
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, branch, new CilOperand.BranchTarget(4)),
            Instruction(3, CilOperation.Return),
            Instruction(4, CilOperation.Return));
        var result = Rule(body).Rewrite(body);
        Assert.Equal(CilOperation.Pop, result.Instructions[1].Operation);
        Assert.Equal(expected, result.Instructions[2].Operation);
        Assert.Equal(expected == CilOperation.Branch ? body.Instructions[2].Operand : new CilOperand.None(),
            result.Instructions[2].Operand);
    }

    [Fact]
    public void PreservesReferenceNullableAndOpenTypeBehavior()
    {
        CliTypeIdentity[] types = [Owner, CliTypeIdentity.GenericParameter(true, 0),
            CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true), [Integer])];
        foreach (var type in types)
        {
            var body = Body(
                Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(type)),
                Instruction(2, CilOperation.LoadNull),
                Instruction(3, CilOperation.CompareEqual),
                Instruction(4, CilOperation.Return));
            Assert.Same(body, Rule(body).Rewrite(body));
        }
    }

    [Fact]
    public void DoesNotCrossAnIncomingBranchTarget()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.LoadNull),
            Instruction(3, CilOperation.CompareEqual),
            Instruction(4, CilOperation.Return),
            Instruction(5, CilOperation.LoadNull),
            Instruction(6, CilOperation.Branch, new CilOperand.BranchTarget(2)));
        Assert.Same(body, Rule(body).Rewrite(body));
    }

    [Fact]
    public void LeavesEscapingBoxesAndNonNullComparisonsAlone()
    {
        var body = Body(
            Instruction(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            Instruction(1, CilOperation.Box, new CilOperand.TypeIdentity(Integer)),
            Instruction(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            Instruction(3, CilOperation.CompareEqual),
            Instruction(4, CilOperation.Return));
        Assert.Same(body, Rule(body).Rewrite(body));
    }

    [Fact]
    public void RejectsMissingDependenciesAndBody()
    {
        var body = Body(Instruction(0, CilOperation.Return));
        Assert.Throws<ArgumentNullException>(() => new BoxedNullComparisonRewriter(null!, new NullableTypeResolver()));
        Assert.Throws<ArgumentNullException>(() => new BoxedNullComparisonRewriter(Graph(body), null!));
        Assert.Throws<ArgumentNullException>(() => Rule(body).Rewrite(null!));
    }

    private static BoxedNullComparisonRewriter Rule(CilMethodBody body) =>
        new(Graph(body), new NullableTypeResolver());
}
