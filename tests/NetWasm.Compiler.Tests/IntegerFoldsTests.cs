using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// s = s + e folds into accumulators of its own in an unrolled loop, unless the
// method takes s's address anywhere: a ref, span or pointer to s would see the
// split accumulators.
public sealed class IntegerFoldsTests
{
    private static readonly ImmutableArray<CliValueKind> Locals = [CliValueKind.I4, CliValueKind.I4, CliValueKind.NativeInt];

    private static readonly ImmutableArray<CilInstruction> Body =
    [
        Instruction(CilOperation.LoadLocal, 0),
        Instruction(CilOperation.LoadLocal, 1),
        new(0, 1, CilOperation.Add, new CilOperand.None()),
        Instruction(CilOperation.StoreLocal, 0),
    ];

    [Fact]
    public void AnAccumulatorTheMethodNeverAddressesFolds()
    {
        var fold = Assert.Single(IntegerFolds.Find(Body, Body, Locals, static _ => true));
        Assert.Equal(0, fold.Accumulator);
        Assert.Equal(CilOperation.Add, fold.Operation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAccumulatorWhoseAddressTheMethodTakesDoesNotFold(bool beforeTheLoop)
    {
        CilInstruction[] address = [Instruction(CilOperation.LoadLocalAddress, 0), Instruction(CilOperation.StoreLocal, 2)];
        ImmutableArray<CilInstruction> method = beforeTheLoop ? [.. address, .. Body] : [.. Body, .. address];

        Assert.Empty(IntegerFolds.Find(Body, method, Locals, static _ => true));
    }

    private static CilInstruction Instruction(CilOperation operation, int index) =>
        new(0, 1, operation, new CilOperand.Index(index));
}
