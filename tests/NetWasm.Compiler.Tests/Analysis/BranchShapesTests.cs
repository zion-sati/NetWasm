// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

// The shapes the branch-to-select rewriters share, over the entry stacks validated IL gives each instruction.
public sealed class BranchShapesTests
{
    private const CliValueKind I4 = CliValueKind.I4;
    private const CliValueKind I8 = CliValueKind.I8;
    private const CliValueKind N = CliValueKind.NativeInt;
    private const CliValueKind F4 = CliValueKind.F4;
    private const CliValueKind F8 = CliValueKind.F8;
    private const CliValueKind O = CliValueKind.ManagedReference;

    // A two-operand branch compares integers of one width (or int32 with a native int) or floats of one width; a
    // float and a double, two references, or a native int and a managed address are left as branches.
    [Theory]
    [InlineData(I4, I4, true)]
    [InlineData(I8, I8, true)]
    [InlineData(N, N, true)]
    [InlineData(I4, N, true)]
    [InlineData(N, I4, true)]
    [InlineData(F4, F4, true)]
    [InlineData(F8, F8, true)]
    [InlineData(F4, F8, false)]
    [InlineData(F8, F4, false)]
    [InlineData(O, O, false)]
    [InlineData(N, CliValueKind.ManagedAddress, false)]
    public void ATwoOperandBranchComparesNumbersOfOneKind(CliValueKind left, CliValueKind right, bool compared) =>
        Assert.Equal(compared, BranchShapes.Condition(CilOperation.BranchIfEqual, [left, right]) is not null);

    // Each branch's comparison and whether it holds when the branch is taken; for floats the unsigned (unordered)
    // forms swap with the signed (ordered) ones when negated.
    [Theory]
    [InlineData(CilOperation.BranchIfEqual, false, CilOperation.CompareEqual, true)]
    [InlineData(CilOperation.BranchIfNotEqual, false, CilOperation.CompareEqual, false)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned, false, CilOperation.CompareGreaterThanSigned, true)]
    [InlineData(CilOperation.BranchIfGreaterThanUnsigned, false, CilOperation.CompareGreaterThanUnsigned, true)]
    [InlineData(CilOperation.BranchIfLessThanSigned, false, CilOperation.CompareLessThanSigned, true)]
    [InlineData(CilOperation.BranchIfLessThanUnsigned, false, CilOperation.CompareLessThanUnsigned, true)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, false, CilOperation.CompareLessThanSigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned, true, CilOperation.CompareLessThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned, false, CilOperation.CompareLessThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned, true, CilOperation.CompareLessThanSigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, false, CilOperation.CompareGreaterThanSigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualSigned, true, CilOperation.CompareGreaterThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned, false, CilOperation.CompareGreaterThanUnsigned, false)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned, true, CilOperation.CompareGreaterThanSigned, false)]
    public void EachBranchHasItsComparison(CilOperation branch, bool floats, CilOperation compare, bool takenWhenTrue)
    {
        var condition = BranchShapes.Condition(branch, floats ? [F8, F8] : [I4, I4])!;

        Assert.Equal((compare, takenWhenTrue, 2), (condition.Instructions.Single().Operation, condition.TakenWhenTrue,
            condition.Operands));
    }

    // brtrue and brfalse test an int32 as it is, and a wider integer against zero; a float or a reference is no
    // condition here.
    [Theory]
    [InlineData(I4, 0, true)]
    [InlineData(I8, 2, false)]
    [InlineData(N, 3, false)]
    public void ABooleanBranchTestsItsIntegerAgainstZero(CliValueKind kind, int instructions, bool takenWhenTrue)
    {
        var condition = BranchShapes.Condition(CilOperation.BranchIfTrue, [kind])!;

        Assert.Equal((instructions, takenWhenTrue, 1), (condition.Instructions.Length, condition.TakenWhenTrue, condition.Operands));
        Assert.Null(BranchShapes.Condition(CilOperation.BranchIfFalse, [O]));
    }

    // Arithmetic on one kind, or on int32 with a native int, computes a number; a float with a double does not.
    // Conversions start from an integer unless they make a float; a shift's count is an int32 or a native int.
    [Theory]
    [InlineData(CilOperation.Add, new[] { I4, I4 }, I4)]
    [InlineData(CilOperation.Add, new[] { I4, N }, N)]
    [InlineData(CilOperation.Add, new[] { N, I4 }, N)]
    [InlineData(CilOperation.Add, new[] { F4, F8 }, null)]
    [InlineData(CilOperation.CompareLessThanSigned, new[] { F8, F8 }, I4)]
    [InlineData(CilOperation.CompareLessThanSigned, new[] { F4, F8 }, null)]
    [InlineData(CilOperation.ShiftLeft, new[] { I8, I4 }, I8)]
    [InlineData(CilOperation.ShiftRightSigned, new[] { N, N }, N)]
    [InlineData(CilOperation.ConvertInt32, new[] { F8 }, null)]
    [InlineData(CilOperation.ConvertFloat64, new[] { F4 }, F8)]
    [InlineData(CilOperation.ConvertNativeInt, new[] { I4 }, N)]
    public void ArithmeticComputesANumber(CilOperation operation, CliValueKind[] entry, CliValueKind? result)
    {
        var effect = BranchShapes.Effect(Body(), new CilInstruction(0, 1, operation, new CilOperand.None()), [.. entry]);

        Assert.Equal(result, effect?.Result);
    }

    // conv.* names its width: native, 64-bit or narrower.
    [Theory]
    [InlineData(true, 32, N)]
    [InlineData(false, 64, I8)]
    [InlineData(false, 16, I4)]
    public void AConversionMakesTheWidthItNames(bool native, int bitWidth, CliValueKind result)
    {
        var conversion = new CilOperand.NumericConversion(bitWidth, DestinationUnsigned: false, Checked: false,
            SourceUnsigned: false, native);

        var effect = BranchShapes.Effect(Body(), new CilInstruction(0, 1, CilOperation.ConvertNumeric, conversion), [I4]);

        Assert.Equal((1, result), effect!.Value);
    }

    // A branch's target is an entry, and the splice renumbers it with everything else.
    [Fact]
    public void ABranchsTargetIsAnEntryAndSurvivesTheSplice()
    {
        var body = Body() with
        {
            Instructions =
            [
                new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                new(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
                new(2, 3, CilOperation.Nop, new CilOperand.None()),
                new(3, 4, CilOperation.Return, new CilOperand.None()),
            ],
        };

        var entries = BranchShapes.EntryCounts(body);
        var spliced = BranchShapes.Splice(body, [new(2, 2, [])]);

        Assert.Equal(1, entries[3]);
        // The removed nop moves the target one back.
        Assert.Equal(new CilOperand.BranchTarget(2), spliced.Instructions[1].Operand);
    }

    private static CilMethodBody Body()
    {
        var assembly = new AssemblyIdentity("Shapes");
        var definition = new MethodDefinitionModel(new(assembly, 0x06000001), new(assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        return new CilMethodBody(definition, 2, [], []);
    }
}
