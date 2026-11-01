using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class SwitchBitTestRewriterTests
{
    private static readonly MethodDefinitionModel Method = new(
        new(new AssemblyIdentity("SwitchFixture"), 0x06000001), new(new AssemblyIdentity("SwitchFixture"), 0x02000001),
        "Classify", true, MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4), 1);

    [Fact]
    public void RejectsAMissingBody() =>
        Assert.Throws<ArgumentNullException>(() => AsRule(new SwitchBitTestRewriter()).Rewrite(null!));

    [Fact]
    public void TestsAMaskForTwoTargets()
    {
        // Cases 0..3: 1 and 3 go to target 100, 0 and 2 to target 200.
        var body = Body(Chain(0, [200, 100, 200, 100]));

        var result = AsRule(new SwitchBitTestRewriter()).Rewrite(body);

        Assert.Equal(
            [
                CilOperation.LoadArgument, CilOperation.Duplicate, CilOperation.LoadInt32, CilOperation.BranchIfGreaterThanOrEqualUnsigned,
                CilOperation.ConvertInt32, CilOperation.StoreLocal, CilOperation.LoadInt32, CilOperation.LoadLocal,
                CilOperation.ShiftRightUnsigned, CilOperation.LoadInt32, CilOperation.BitwiseAnd, CilOperation.BranchIfTrue,
                CilOperation.Branch,
            ],
            result.Instructions.Take(13).Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.ConstantI4(4), result.Instructions[2].Operand);
        // The out-of-range branch reaches the kept final pop, then the default.
        Assert.Equal(new CilOperand.BranchTarget(21), result.Instructions[3].Operand);
        Assert.Equal(CilOperation.Pop, result.Instructions[21].Operation);
        Assert.Equal(new CilOperand.ConstantI4(0b0101), result.Instructions[6].Operand);
        Assert.Equal(new CilOperand.BranchTarget(200), result.Instructions[11].Operand);
        Assert.Equal(new CilOperand.BranchTarget(100), result.Instructions[12].Operand);
        Assert.All(result.Instructions.Skip(13).Take(8), instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
        Assert.Equal(new CilOperand.Index(1), result.Instructions[5].Operand);
        Assert.Equal(new[] { CliValueKind.I4, CliValueKind.I4 }, result.Locals.ToArray());
        Assert.Equal(CliTypeIdentity.Primitive("i4", CliValueKind.I4), result.LocalSignatureTypes[^1]);
        Assert.Equal(body.Instructions.Select(instruction => instruction.Offset), result.Instructions.Select(instruction => instruction.Offset));
    }

    [Fact]
    public void ChecksTheRangeForASingleTarget()
    {
        var body = Body(Chain(0, [100, 100, 100]));

        var result = AsRule(new SwitchBitTestRewriter()).Rewrite(body);

        Assert.Equal([CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.BranchIfLessThanUnsigned, CilOperation.Branch],
            result.Instructions.Take(4).Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.BranchTarget(100), result.Instructions[2].Operand);
        // Past the final pop, which the range check makes unnecessary.
        Assert.Equal(new CilOperand.BranchTarget(17), result.Instructions[3].Operand);
        Assert.All(result.Instructions.Skip(4).Take(13), instruction => Assert.Equal(CilOperation.Nop, instruction.Operation));
        Assert.Single(result.Locals);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void LeavesOtherShapesAlone(int shape)
    {
        var instructions = shape switch
        {
            0 => Chain(0, [100, 200, 300]),
            1 => Chain(0, [100, 200]),
            2 => Chain(0, [.. Enumerable.Repeat(100, SwitchBitTestRewriter.MaximumCases + 1)]),
            _ => Chain(0, [100, 200, 100]),
        };
        var body = Body(instructions);
        body = shape switch
        {
            // A branch into the middle of the chain.
            3 => body with
            {
                Instructions = [.. body.Instructions,
                new(body.Instructions.Length, body.Instructions.Length + 1, CilOperation.Branch, new CilOperand.BranchTarget(6))]
            },
            // An exception region boundary inside the chain.
            4 => body with { ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 6, 6, 2, null, null)] },
            // No final pop.
            5 => body with { Instructions = Replace(body.Instructions, 16, CilOperation.Nop, new CilOperand.None()) },
            // A case constant out of order.
            6 => body with { Instructions = Replace(body.Instructions, 7, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)) },
            // A comparison that skips past the next case.
            7 => body with { Instructions = Replace(body.Instructions, 3, CilOperation.BranchIfNotEqual, new CilOperand.BranchTarget(11)) },
            // A filter that starts inside the chain.
            9 => body with { ExceptionRegions = [new(CilExceptionRegionKind.Filter, 17, 1, 18, 1, null, 6)] },
            // Operands that are not a case constant or a branch target.
            10 => body with { Instructions = Replace(body.Instructions, 2, CilOperation.LoadInt32, new CilOperand.None()) },
            11 => body with { Instructions = Replace(body.Instructions, 5, CilOperation.Branch, new CilOperand.None()) },
            // A case without its pop or its branch.
            12 => body with { Instructions = Replace(body.Instructions, 4, CilOperation.Nop, new CilOperand.None()) },
            13 => body with { Instructions = Replace(body.Instructions, 5, CilOperation.Nop, new CilOperand.BranchTarget(100)) },
            _ => body,
        };

        Assert.Same(body, AsRule(new SwitchBitTestRewriter()).Rewrite(body));
    }

    [Fact]
    public void RewritesEachChainWithItsOwnLocal()
    {
        var first = Chain(0, [100, 200, 100]);
        var second = Chain(first.Length, [100, 200, 200]);
        var body = Body([.. first, .. second]);

        var result = AsRule(new SwitchBitTestRewriter()).Rewrite(body);

        Assert.Equal([new CilOperand.Index(1), new CilOperand.Index(2)], result.Instructions
            .Where(instruction => instruction.Operation == CilOperation.StoreLocal).Select(instruction => instruction.Operand));
        Assert.Equal(3, result.Locals.Length);
    }

    [Fact]
    public void ToleratesAChainAtTheEndOfTheBody()
    {
        var body = Body([.. Chain(0, [100, 200, 100]).SkipLast(1)]);

        Assert.Same(body, AsRule(new SwitchBitTestRewriter()).Rewrite(body));
    }

    // ldarg.0, then the lowering of `switch` over the targets, then the final pop.
    private static CilInstruction[] Chain(int start, int[] targets)
    {
        var instructions = new List<CilInstruction> { Instruction(start, CilOperation.LoadArgument, new CilOperand.Index(0)) };
        for (var index = 0; index < targets.Length; index++)
        {
            var at = start + 1 + index * 5;
            instructions.Add(Instruction(at, CilOperation.Duplicate, new CilOperand.None()));
            instructions.Add(Instruction(at + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(index)));
            instructions.Add(Instruction(at + 2, CilOperation.BranchIfNotEqual, new CilOperand.BranchTarget(at + 5)));
            instructions.Add(Instruction(at + 3, CilOperation.Pop, new CilOperand.None()));
            instructions.Add(Instruction(at + 4, CilOperation.Branch, new CilOperand.BranchTarget(targets[index])));
        }
        instructions.Add(Instruction(start + 1 + targets.Length * 5, CilOperation.Pop, new CilOperand.None()));
        return [.. instructions];
    }

    private static System.Collections.Immutable.ImmutableArray<CilInstruction> Replace(
        System.Collections.Immutable.ImmutableArray<CilInstruction> instructions, int index, CilOperation operation, CilOperand operand) =>
        instructions.SetItem(index, instructions[index] with { Operation = operation, Operand = operand });

    private static CilInstruction Instruction(int offset, CilOperation operation, CilOperand operand) =>
        new(offset, offset + 1, operation, operand);

    private static CilMethodBody Body(CilInstruction[] instructions) => new(Method, 3, [CliValueKind.I4], [.. instructions])
    {
        LocalSignatureTypes = [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
    };

    private static IMethodRewriteRule AsRule(IMethodRewriteRule rule) => rule;
}
