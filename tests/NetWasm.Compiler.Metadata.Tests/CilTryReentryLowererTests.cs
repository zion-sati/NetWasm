using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// IL may enter a try other than by falling into it: a branch from outside to its first
// instruction, and a catch's leave to any instruction of its own try (ECMA-335 III.3.46; issue
// 221's class). Each such try gets an entry point before it that falls into it, and a re-entered
// try a dispatch of its targets on an entry local.
public sealed class CilTryReentryLowererTests
{
    private static readonly EntityKey Exception = new(new AssemblyIdentity("Tests"), 0x02000001);

    [Fact]
    public void AMethodWithoutRegionsOrEntriesIsUnchanged()
    {
        ImmutableArray<CilInstruction> plain = [I(0, CilOperation.Branch, 1), I(1, CilOperation.Return)];
        ImmutableArray<CilInstruction> guarded =
        [
            I(0, CilOperation.Nop), I(1, CilOperation.Leave, 4), I(2, CilOperation.Nop), I(3, CilOperation.EndFinally),
            I(4, CilOperation.Return),
        ];
        ImmutableArray<CilExceptionRegion> finallyRegion = [Finally(0, 2, 2, 2)];

        var withoutRegions = Lower(plain, []);
        var withoutEntries = Lower(guarded, finallyRegion);

        Assert.Equal(plain, withoutRegions.Instructions);
        Assert.Equal(guarded, withoutEntries.Instructions);
        Assert.Equal(finallyRegion, withoutEntries.ExceptionRegions);
        Assert.Empty(withoutEntries.AddedLocals);
        Assert.Equal(0, withoutEntries.AdditionalMaxStack);
    }

    // A loop whose back edge branches to the try's first instruction from outside it branches to
    // an entry point just before the try instead, which falls into it.
    [Fact]
    public void AnOutsideBranchToATrysFirstInstructionGoesToItsEntryPoint()
    {
        var lowered = Lower(
        [
            I(0, CilOperation.Nop), I(1, CilOperation.Nop), I(2, CilOperation.Leave, 5), I(3, CilOperation.Nop),
            I(4, CilOperation.EndFinally), I(5, CilOperation.LoadArgument, 0), I(6, CilOperation.BranchIfTrue, 1),
            I(7, CilOperation.Return),
        ], [Finally(1, 2, 3, 2)]);

        Assert.Equal(
            [CilOperation.Nop, CilOperation.Nop, CilOperation.Nop, CilOperation.Leave, CilOperation.Nop,
                CilOperation.EndFinally, CilOperation.LoadArgument, CilOperation.BranchIfTrue, CilOperation.Return],
            lowered.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(1, Target(lowered.Instructions[7]));
        Assert.Equal(6, Target(lowered.Instructions[3]));
        var region = Assert.Single(lowered.ExceptionRegions);
        Assert.Equal((2, 2, 4, 2), (region.TryOffset, region.TryLength, region.HandlerOffset, region.HandlerLength));
        Assert.Empty(lowered.AddedLocals);
        Assert.Equal(0, lowered.AdditionalMaxStack);
        Assert.Equal(1, lowered.Instructions[1].SourceOffset);
    }

    // A catch that leaves back into its own try stores the target's number and leaves to the
    // entry point; the try's dispatch resets the local and branches to the target.
    [Fact]
    public void ACatchReentersItsTryThroughTheEntryPointsDispatch()
    {
        var lowered = Lower(
        [
            I(0, CilOperation.Nop), I(1, CilOperation.Nop), I(2, CilOperation.Nop), I(3, CilOperation.Leave, 7),
            I(4, CilOperation.Pop), I(5, CilOperation.Leave, 2), I(6, CilOperation.Nop), I(7, CilOperation.Return),
        ], [Catch(1, 3, 4, 2)], localCount: 3);

        Assert.Equal(
        [
            "nop", "nop", "ldloc 3", "ldc 1", "beq 6", "br 9", "ldc 0", "stloc 3", "br 10", "nop", "nop", "leave 17",
            "pop", "ldc 1", "stloc 3", "leave 1", "nop", "ret",
        ], lowered.Instructions.Select(Describe));
        var region = Assert.Single(lowered.ExceptionRegions);
        Assert.Equal((2, 10, 12, 4), (region.TryOffset, region.TryLength, region.HandlerOffset, region.HandlerLength));
        Assert.Equal([CliTypeIdentity.FromStackKind(CliValueKind.I4)], lowered.AddedLocals);
        Assert.Equal(2, lowered.AdditionalMaxStack);
    }

    // The target may begin a try nested in the catch's own try, entered at its first instruction:
    // the dispatch branches to the nested try's entry point. A catch's leave to its try's own
    // first instruction lands after the dispatch.
    [Fact]
    public void ACatchMayReenterAtANestedTrysFirstInstructionOrItsOwnTrysFirst()
    {
        var lowered = Lower(
        [
            I(0, CilOperation.Nop), I(1, CilOperation.Leave, 4), I(2, CilOperation.Nop), I(3, CilOperation.EndFinally),
            I(4, CilOperation.Leave, 8), I(5, CilOperation.Pop), I(6, CilOperation.Leave, 0), I(7, CilOperation.Leave, 0),
            I(8, CilOperation.Return),
        ], [Finally(0, 2, 2, 2), Catch(0, 5, 5, 3)]);

        Assert.Equal(
        [
            "nop", "ldloc 0", "ldc 1", "beq 5", "br 8", "ldc 0", "stloc 0", "br 8", "nop", "nop", "leave 13", "nop",
            "endfinally", "leave 21", "pop", "ldc 1", "stloc 0", "leave 0", "ldc 1", "stloc 0", "leave 0", "ret",
        ], lowered.Instructions.Select(Describe).Take(22));
        Assert.Equal(
            [(9, 2, 11, 2), (1, 13, 14, 7)],
            lowered.ExceptionRegions.Select(region => (region.TryOffset, region.TryLength, region.HandlerOffset,
                region.HandlerLength)));
    }

    // Blocks around an entered try: one before it ends at its entry point, one nested in it from
    // its first instruction starts after the entry point, and a filter moves with its first
    // instruction.
    [Fact]
    public void BlocksAroundAnEnteredTryKeepTheirBoundsAroundItsEntryPoint()
    {
        var lowered = Lower(
        [
            I(0, CilOperation.Leave, 2), I(1, CilOperation.EndFinally), I(2, CilOperation.Nop),
            I(3, CilOperation.Leave, 6), I(4, CilOperation.Nop), I(5, CilOperation.EndFinally),
            I(6, CilOperation.Leave, 11), I(7, CilOperation.Pop), I(8, CilOperation.LoadInt32, 1),
            I(9, CilOperation.EndFilter), I(10, CilOperation.Leave, 11), I(11, CilOperation.LoadArgument, 0),
            I(12, CilOperation.BranchIfTrue, 2), I(13, CilOperation.Return),
        ],
        [
            Finally(0, 1, 1, 1), Finally(2, 2, 4, 2),
            new CilExceptionRegion(CilExceptionRegionKind.Filter, 2, 5, 10, 1, null, 7),
        ]);

        Assert.Equal(2, Target(lowered.Instructions[13]));
        Assert.Equal(
            [(0, 1, 1, 1, (int?)null), (3, 2, 5, 2, null), (3, 5, 11, 1, 8)],
            lowered.ExceptionRegions.Select(region => (region.TryOffset, region.TryLength, region.HandlerOffset,
                region.HandlerLength, region.FilterOffset)));
    }

    // A block that encloses the entered try and starts where it does starts at the entry point,
    // so the entry point stays inside it.
    [Fact]
    public void ABlockEnclosingAnEnteredTryFromItsStartContainsTheEntryPoint()
    {
        var lowered = Lower(
        [
            I(0, CilOperation.Nop), I(1, CilOperation.Leave, 4), I(2, CilOperation.Nop), I(3, CilOperation.EndFinally),
            I(4, CilOperation.LoadArgument, 0), I(5, CilOperation.BranchIfTrue, 0), I(6, CilOperation.Leave, 9),
            I(7, CilOperation.Nop), I(8, CilOperation.EndFinally), I(9, CilOperation.Return),
        ], [Finally(0, 2, 2, 2), Finally(0, 7, 7, 2)]);

        Assert.Equal(0, Target(lowered.Instructions[6]));
        Assert.Equal(
            [(1, 2, 3, 2), (0, 8, 8, 2)],
            lowered.ExceptionRegions.Select(region => (region.TryOffset, region.TryLength, region.HandlerOffset,
                region.HandlerLength)));
    }

    // Only a catch's own try is re-entered: a leave from one try's catch into another try stays as
    // it is, for the region validator to refuse.
    [Fact]
    public void ALeaveIntoAnotherTryIsNoReentry()
    {
        ImmutableArray<CilInstruction> instructions =
        [
            I(0, CilOperation.Nop), I(1, CilOperation.Leave, 7), I(2, CilOperation.Pop), I(3, CilOperation.Leave, 5),
            I(4, CilOperation.Nop), I(5, CilOperation.Nop), I(6, CilOperation.Leave, 7), I(7, CilOperation.Return),
        ];

        var lowered = Lower(instructions, [Catch(0, 2, 2, 2), Finally(5, 2, 4, 1)]);

        Assert.Equal(instructions, lowered.Instructions);
    }

    private static LoweredTryReentryBody Lower(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> regions,
        int localCount = 0) =>
        ((ICilTryReentryLowerer)new CilTryReentryLowerer()).Lower(instructions, regions, localCount);

    private static CilInstruction I(int offset, CilOperation operation, int? operand = null) =>
        new(offset, offset + 1, operation, operation switch
        {
            CilOperation.LoadArgument or CilOperation.LoadLocal => new CilOperand.Index(operand!.Value),
            CilOperation.LoadInt32 => new CilOperand.ConstantI4(operand!.Value),
            _ when operand is int target => new CilOperand.BranchTarget(target),
            _ => new CilOperand.None(),
        });

    private static int Target(CilInstruction instruction) => ((CilOperand.BranchTarget)instruction.Operand).Offset;

    private static string Describe(CilInstruction instruction) => (instruction.Operation, instruction.Operand) switch
    {
        (CilOperation.Nop, _) => "nop",
        (CilOperation.Pop, _) => "pop",
        (CilOperation.Return, _) => "ret",
        (CilOperation.EndFinally, _) => "endfinally",
        (CilOperation.LoadLocal, CilOperand.Index local) => $"ldloc {local.Value}",
        (CilOperation.StoreLocal, CilOperand.Index local) => $"stloc {local.Value}",
        (CilOperation.LoadInt32, CilOperand.ConstantI4 value) => $"ldc {value.Value}",
        (CilOperation.BranchIfEqual, CilOperand.BranchTarget target) => $"beq {target.Offset}",
        (CilOperation.Branch, CilOperand.BranchTarget target) => $"br {target.Offset}",
        (CilOperation.Leave, CilOperand.BranchTarget target) => $"leave {target.Offset}",
        _ => instruction.ToString(),
    };

    private static CilExceptionRegion Finally(int tryOffset, int tryLength, int handlerOffset, int handlerLength) =>
        new(CilExceptionRegionKind.Finally, tryOffset, tryLength, handlerOffset, handlerLength, null, null);

    private static CilExceptionRegion Catch(int tryOffset, int tryLength, int handlerOffset, int handlerLength) =>
        new(CilExceptionRegionKind.Catch, tryOffset, tryLength, handlerOffset, handlerLength, Exception, null);
}
