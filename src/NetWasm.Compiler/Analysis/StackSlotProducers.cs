using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Facts about one body: the instruction that pushed a value another
// instruction consumes, from the body's typed entry stacks, when it was pushed
// in the same block and no instruction between them reads or replaces that
// stack slot.
internal sealed record StackSlotProducers(
    CilMethodBody Body,
    IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> Stacks)
{
    private readonly HashSet<int> _entries = InstructionEntries.Of(Body);
    private Dictionary<int, int>? _soleBranches;

    // Instructions that push nothing.
    private static readonly FrozenSet<CilOperation> PushNothing = new[]
    {
        CilOperation.Nop, CilOperation.StoreArgument, CilOperation.StoreLocal, CilOperation.Pop,
        CilOperation.StoreField, CilOperation.StoreStaticField, CilOperation.StoreObject, CilOperation.CopyObject,
        CilOperation.InitializeObject, CilOperation.CopyBlock, CilOperation.InitializeBlock,
        CilOperation.StoreArrayElement, CilOperation.StoreArrayElementReference, CilOperation.StoreRectangularArrayElement,
        CilOperation.BranchIfTrue, CilOperation.BranchIfFalse, CilOperation.BranchIfEqual, CilOperation.BranchIfNotEqual,
        CilOperation.BranchIfGreaterThanSigned, CilOperation.BranchIfGreaterThanUnsigned,
        CilOperation.BranchIfGreaterThanOrEqualSigned, CilOperation.BranchIfGreaterThanOrEqualUnsigned,
        CilOperation.BranchIfLessThanSigned, CilOperation.BranchIfLessThanUnsigned,
        CilOperation.BranchIfLessThanOrEqualSigned, CilOperation.BranchIfLessThanOrEqualUnsigned,
    }.ToFrozenSet();

    // Instructions that push exactly one value, whatever they consume.
    private static readonly FrozenSet<CilOperation> PushOne = new[]
    {
        CilOperation.LoadArgument, CilOperation.LoadArgumentAddress, CilOperation.LoadLocal, CilOperation.LoadLocalAddress,
        CilOperation.LoadInt32, CilOperation.LoadInt64, CilOperation.LoadFloat32, CilOperation.LoadFloat64,
        CilOperation.LoadNull, CilOperation.LoadString, CilOperation.LoadField, CilOperation.LoadFieldAddress,
        CilOperation.LoadStaticField, CilOperation.LoadStaticFieldAddress, CilOperation.LoadObject,
        CilOperation.LoadArrayLength, CilOperation.LoadArrayElement, CilOperation.LoadArrayElementAddress,
        CilOperation.LoadArrayElementReference, CilOperation.NewArray, CilOperation.Box, CilOperation.Unbox,
        CilOperation.UnboxAny, CilOperation.CastClass, CilOperation.IsInstance,
        CilOperation.ConvertNumeric, CilOperation.ConvertNativeInt, CilOperation.ConvertNativeUInt,
        CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply, CilOperation.Divide, CilOperation.DivideUnsigned,
        CilOperation.Remainder, CilOperation.RemainderUnsigned, CilOperation.AddChecked, CilOperation.AddCheckedUnsigned,
        CilOperation.SubtractChecked, CilOperation.SubtractCheckedUnsigned, CilOperation.MultiplyChecked,
        CilOperation.MultiplyCheckedUnsigned, CilOperation.BitwiseAnd, CilOperation.BitwiseOr, CilOperation.BitwiseXor,
        CilOperation.ShiftLeft, CilOperation.ShiftRightSigned, CilOperation.ShiftRightUnsigned, CilOperation.Negate,
        CilOperation.OnesComplement, CilOperation.CompareEqual, CilOperation.CompareGreaterThanSigned,
        CilOperation.CompareGreaterThanUnsigned, CilOperation.CompareLessThanSigned, CilOperation.CompareLessThanUnsigned,
    }.ToFrozenSet();

    // Whether control can reach the instruction other than from the one before it.
    public bool Enters(int index) => _entries.Contains(Body.Instructions[index].Offset);

    // The producer of the value on top of the consumer's entry stack.
    public int? FindTop(int consumer, bool acrossBranches = false) =>
        Stacks.TryGetValue(Body.Instructions[consumer].Offset, out var stack) && stack.Length > 0
            ? Find(consumer, stack.Length - 1, acrossBranches: acrossBranches)
            : null;

    // The producer of the value in `slot`, counted from the bottom of the
    // consumer's entry stack, which must be known. With `duplicates`, a dup
    // counts as the producer of the lower copy it leaves as well as the top one.
    // With `acrossBranches`, the walk continues from an instruction that only
    // one earlier branch reaches, with nothing falling into it, at that branch,
    // as past a bounds check's branch around its throw.
    public int? Find(int consumer, int slot, bool duplicates = false, bool acrossBranches = false)
    {
        var after = Stacks[Body.Instructions[consumer].Offset].Length;
        for (var index = consumer - 1; index >= 0; index--)
        {
            // Another path could reach the consumer with a different value.
            if (_entries.Contains(Body.Instructions[index + 1].Offset))
            {
                if (!acrossBranches || !SoleBranches().TryGetValue(index + 1, out var branch)) return null;
                index = branch;
            }
            if (!Stacks.TryGetValue(Body.Instructions[index].Offset, out var stack))
            {
                return null;
            }
            // Depth grows by one at most per instruction, so one that starts
            // at the slot's depth pushed it, as did one that is certain to
            // push a single value just below the depth after it.
            var pushed = Pushed(Body.Instructions[index]);
            if (stack.Length == slot || pushed == 1 && after - 1 == slot)
            {
                return index;
            }
            // An instruction's lowest stack slot is the depth after it, less
            // what it pushed: one value at most, or two for dup, which reads
            // the slot below. Depth grows by one at most per instruction, so
            // the walk meets the slot's depth before any lower one.
            if (after - (pushed ?? 1) <= slot)
            {
                return duplicates && Body.Instructions[index].Operation == CilOperation.Duplicate && after - 2 == slot
                    ? index
                    : null;
            }
            after = stack.Length;
        }
        return null;
    }

    // Instructions that only one earlier branch reaches, by the branch's index:
    // the instruction before them cannot fall into them and nothing else
    // targets them.
    private Dictionary<int, int> SoleBranches()
    {
        if (_soleBranches is not null) return _soleBranches;
        var sources = new Dictionary<int, List<int>>();
        var indices = new Dictionary<int, int>();
        for (var index = 0; index < Body.Instructions.Length; index++)
        {
            indices[Body.Instructions[index].Offset] = index;
        }
        var excluded = new HashSet<int>();
        for (var index = 0; index < Body.Instructions.Length; index++)
        {
            switch (Body.Instructions[index].Operand)
            {
                case CilOperand.BranchTarget { Offset: var target } when indices.TryGetValue(target, out var targetIndex):
                    if (!sources.TryGetValue(targetIndex, out var list)) sources[targetIndex] = list = [];
                    list.Add(index);
                    break;
                case CilOperand.SwitchTargets { Offsets: var targets }:
                    foreach (var target in targets)
                    {
                        if (indices.TryGetValue(target, out var switchTarget)) excluded.Add(switchTarget);
                    }
                    break;
            }
        }
        foreach (var region in Body.ExceptionRegions)
        {
            int[] boundaries =
            [
                region.TryOffset, region.TryOffset + region.TryLength, region.HandlerOffset,
                region.HandlerOffset + region.HandlerLength, region.FilterOffset ?? -1,
            ];
            foreach (var boundary in boundaries)
            {
                if (indices.TryGetValue(boundary, out var boundaryIndex)) excluded.Add(boundaryIndex);
            }
        }
        _soleBranches = [];
        foreach (var (target, list) in sources)
        {
            if (list.Count == 1 && list[0] < target && target > 0 && !excluded.Contains(target) &&
                Body.Instructions[target - 1].Operation is CilOperation.Throw or CilOperation.Rethrow or
                    CilOperation.Branch or CilOperation.Return or CilOperation.Leave or CilOperation.EndFinally)
            {
                _soleBranches[target] = list[0];
            }
        }
        return _soleBranches;
    }

    // How many values an instruction pushes, where that is certain.
    private static int? Pushed(CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.Duplicate } => 2,
        { Operation: var operation } when PushNothing.Contains(operation) => 0,
        { Operation: var operation } when PushOne.Contains(operation) => 1,
        { Operation: CilOperation.NewObject } => 1,
        { Operation: CilOperation.Call or CilOperation.CallVirtual, Operand: CilOperand.MethodInstance method } =>
            method.Value.Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        _ => null,
    };
}
