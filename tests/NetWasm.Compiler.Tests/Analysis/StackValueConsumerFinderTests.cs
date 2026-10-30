using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class StackValueConsumerFinderTests
{
    private static readonly StackValueConsumerFinder Consumers = new(new StackEffectResolver(new NoCalls()));

    // A conditional branch takes the value it tests.
    [Theory]
    [InlineData(CilOperation.BranchIfTrue)]
    [InlineData(CilOperation.BranchIfFalse)]
    public void AConditionalBranchConsumesTheValueItTests(CilOperation branch)
    {
        ImmutableArray<CilInstruction> instructions =
        [
            Load(0, 0), new(1, 2, branch, new CilOperand.BranchTarget(3)), Load(2, 1),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ];

        Assert.Equal(1, Consumers.Find(instructions, [3], 0));
    }

    // A value below a branch's operand also leaves on the branch, where this
    // block cannot follow it.
    [Fact]
    public void AValueBelowABranchHasNoConsumerInItsBlock()
    {
        ImmutableArray<CilInstruction> instructions =
        [
            Load(0, 0), Load(1, 1), new(2, 3, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(4)),
            new(3, 4, CilOperation.Pop, new CilOperand.None()),
            new(4, 5, CilOperation.Pop, new CilOperand.None()),
        ];

        Assert.Null(Consumers.Find(instructions, [4], 0));
    }

    private static CilInstruction Load(int offset, int local) =>
        new(offset, offset + 1, CilOperation.LoadLocal, new CilOperand.Index(local));

    private sealed class NoCalls : ICalledMethodResolver
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => null;
    }
}
