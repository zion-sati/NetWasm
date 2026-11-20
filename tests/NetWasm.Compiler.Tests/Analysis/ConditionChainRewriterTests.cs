// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// `a && b` and `a || b` compile to two branches; when the second condition only computes numbers, both are computed
// and one branch takes their combination.
public sealed class ConditionChainRewriterTests
{
    private static readonly AssemblyIdentity Fixture = new("fixture");

    [Fact]
    public void RejectsMissingCollaboratorsAndBodies()
    {
        var program = new Program();
        var validator = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(program, program, program);
        Assert.Throws<ArgumentNullException>(() => new ConditionChainRewriter(null!, validator));
        Assert.Throws<ArgumentNullException>(() => new ConditionChainRewriter(new ControlFlowGraphBuilderFactory().Create(), null!));
        Assert.Throws<ArgumentNullException>(() => Rewriter().Rewrite(null!));
    }

    // if (a <= b && a <= c) return 1; return 2;  ->  branch to the second return on (a > b) | (a > c).
    [Fact]
    public void AnAndChainBranchesOnceOnTheUnionOfItsExits()
    {
        var body = Body(
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.LoadArgument, Arg(1)),
            I(2, CilOperation.BranchIfGreaterThanSigned, Target(8)),
            I(3, CilOperation.LoadArgument, Arg(0)), I(4, CilOperation.LoadArgument, Arg(2)),
            I(5, CilOperation.BranchIfGreaterThanSigned, Target(8)),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(7, CilOperation.Return),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(9, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.CompareGreaterThanSigned,
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.CompareGreaterThanSigned,
                CilOperation.BitwiseOr, CilOperation.BranchIfTrue,
                CilOperation.LoadInt32, CilOperation.Return, CilOperation.LoadInt32, CilOperation.Return,
            ],
            Operations(result));
        Assert.Equal(new CilOperand.BranchTarget(10), result.Instructions[7].Operand);
        _ = Validate(result);
    }

    // if (a == 0 || b >= c) return 1; return 2;  ->  branch to the second return on (a != 0) & (b < c): the first
    // condition negated, as 0 or 1 whatever a holds.
    [Fact]
    public void AnOrChainBranchesOnceWhenNeitherHolds()
    {
        var body = Body(
            I(0, CilOperation.LoadArgument, Arg(0)),
            I(1, CilOperation.BranchIfFalse, Target(5)),
            I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.LoadArgument, Arg(2)),
            I(4, CilOperation.BranchIfLessThanSigned, Target(7)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(6, CilOperation.Return),
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(8, CilOperation.Return));

        var result = Rewriter().Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.CompareGreaterThanUnsigned,
                CilOperation.LoadArgument, CilOperation.LoadArgument, CilOperation.CompareLessThanSigned,
                CilOperation.BitwiseAnd, CilOperation.BranchIfTrue,
                CilOperation.LoadInt32, CilOperation.Return, CilOperation.LoadInt32, CilOperation.Return,
            ],
            Operations(result));
        _ = Validate(result);
    }

    public static TheoryData<string, CilMethodBody> Unmergeable => new()
    {
        // A second condition that divides could trap where the first branch skipped it.
        { "divide", Body(
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfFalse, Target(7)),
            I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.LoadArgument, Arg(2)), I(4, CilOperation.Divide),
            I(5, CilOperation.BranchIfFalse, Target(7)),
            I(6, CilOperation.Nop),
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(8, CilOperation.Return)) },
        // Branches to unrelated targets are not a chain.
        { "targets", Body(
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfFalse, Target(6)),
            I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.BranchIfFalse, Target(8)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(5, CilOperation.Return),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(7, CilOperation.Return),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)), I(9, CilOperation.Return)) },
        // A jump into the second condition.
        { "entered", Body(
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfFalse, Target(3)),
            I(2, CilOperation.Nop),
            I(3, CilOperation.LoadArgument, Arg(1)), I(4, CilOperation.BranchIfFalse, Target(7)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(6, CilOperation.Return),
            I(7, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)), I(8, CilOperation.Return)) },
    };

    [Theory]
    [MemberData(nameof(Unmergeable))]
    public void ChainsItCannotMergeAreKept(string shape, CilMethodBody body)
    {
        Assert.NotEmpty(shape);
        Assert.Same(body, Rewriter().Rewrite(body));
    }

    [Fact]
    public void BodiesWithOffsetProofsAreKept()
    {
        var body = Body(
            I(0, CilOperation.LoadArgument, Arg(0)), I(1, CilOperation.BranchIfFalse, Target(4)),
            I(2, CilOperation.LoadArgument, Arg(1)), I(3, CilOperation.BranchIfFalse, Target(4)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), I(5, CilOperation.Return));
        Assert.NotSame(body, Rewriter().Rewrite(body));
        var proven = body with { ArrayBoundsProof = new([], [], [], []) };
        Assert.Same(proven, Rewriter().Rewrite(proven));
        var checkedBody = body with { IntegerCheckProof = new([], [], []) };
        Assert.Same(checkedBody, Rewriter().Rewrite(checkedBody));
    }

    private static ConditionChainRewriter Rewriter()
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

    // A static method of three int32 arguments returning an int32.
    private static CilMethodBody Body(params CilInstruction[] instructions) =>
        new(new MethodDefinitionModel(new(Fixture, 0x06000001), new(Fixture, 0x02000002), "Run", true,
                new MethodSignatureModel(CliValueKind.I4, [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4]), 1),
            3, ImmutableArray<CliValueKind>.Empty, [.. instructions]);

    private static CilInstruction I(int offset, CilOperation operation, CilOperand? operand = null) =>
        new(offset, offset + 1, operation, operand ?? new CilOperand.None());

    private static CilOperand.Index Arg(int index) => new(index);

    private static CilOperand.BranchTarget Target(int offset) => new(offset);

    private static CilOperation[] Operations(CilMethodBody body) => [.. body.Instructions.Select(instruction => instruction.Operation)];

    private sealed class Program : ITypeRepository, IFieldRepository, IMethodRepository
    {
        public TypeDefinitionModel GetTypeDefinition(EntityKey key) => throw new NotSupportedException();

        public FieldDefinitionModel GetField(EntityKey key) => throw new NotSupportedException();

        public MethodDefinitionModel GetMethod(EntityKey key) => throw new NotSupportedException();
    }
}
