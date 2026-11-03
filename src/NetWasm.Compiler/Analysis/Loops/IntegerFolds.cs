using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

// A statement of a loop body, s = s op e, where op is add, or, xor or and over
// a 32- or 64-bit integer: wrapping integer arithmetic reassociates, so an
// unrolled body's copies may fold into accumulators of their own, which start
// at the operation's identity and join s when the unrolled loop ends.
internal sealed record IntegerFold(int Accumulator, CilOperation Operation, CliValueKind Kind)
{
    // 0, or all ones for and.
    public CilInstruction Identity() => Kind == CliValueKind.I8
        ? new(0, 1, CilOperation.LoadInt64, new CilOperand.ConstantI8(Operation == CilOperation.BitwiseAnd ? -1 : 0))
        : new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(Operation == CilOperation.BitwiseAnd ? -1 : 0));
}

internal static class IntegerFolds
{
    // The statements `ldloc s; e; op; stloc s` whose accumulator s, an integer
    // local the loop owns, the body reads and writes nowhere else, and whose
    // expression e pushes one value without reaching beneath it to s. The
    // method never takes s's address: a ref, span or pointer to it would see
    // the split accumulators.
    public static IntegerFold[] Find(ImmutableArray<CilInstruction> body, ImmutableArray<CilInstruction> method,
        ImmutableArray<CliValueKind> locals, Func<int, bool> owned)
    {
        var exposed = ExposedSlots.Of(method);
        var folds = new List<IntegerFold>();
        for (var start = 0; start < body.Length; start++)
        {
            if (body[start] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } ||
                (uint)local >= (uint)locals.Length || locals[local] is not (CliValueKind.I4 or CliValueKind.I8) ||
                !owned(local) || exposed.Contains((false, local)) ||
                body.Count(instruction => instruction.Operand is CilOperand.Index { Value: var other } && other == local &&
                    instruction.Operation is CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress) != 2)
            {
                continue;
            }
            var depth = 1;
            for (var index = start + 1; index + 1 < body.Length; index++)
            {
                var instruction = body[index];
                if (depth == 2 && instruction.Operation is CilOperation.Add or CilOperation.BitwiseOr or
                        CilOperation.BitwiseXor or CilOperation.BitwiseAnd &&
                    body[index + 1] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var stored } } &&
                    stored == local)
                {
                    folds.Add(new IntegerFold(local, instruction.Operation, locals[local]));
                    break;
                }
                if (Shape(instruction) is not var (pops, pushes) || pops >= depth)
                {
                    break;
                }
                depth += pushes - pops;
            }
        }
        return [.. folds];
    }

    // What an instruction pops and pushes, for those a fold's expression may hold.
    private static (int Pops, int Pushes)? Shape(CilInstruction instruction) => Shapes.GetValueOrDefault(instruction.Operation);

    private static readonly FrozenDictionary<CilOperation, (int Pops, int Pushes)?> Shapes =
        new (CilOperation[] Operations, (int Pops, int Pushes)? Shape)[]
        {
            ([CilOperation.Nop], (0, 0)),
            ([CilOperation.Duplicate], (1, 2)),
            ([CilOperation.Pop, CilOperation.StoreLocal], (1, 0)),
            ([CilOperation.LoadLocal, CilOperation.LoadArgument, CilOperation.LoadInt32, CilOperation.LoadInt64,
                CilOperation.LoadFloat32, CilOperation.LoadFloat64], (0, 1)),
            ([CilOperation.Add, CilOperation.Subtract, CilOperation.Multiply, CilOperation.BitwiseAnd,
                CilOperation.BitwiseOr, CilOperation.BitwiseXor, CilOperation.ShiftLeft,
                CilOperation.ShiftRightSigned, CilOperation.ShiftRightUnsigned, CilOperation.CompareEqual,
                CilOperation.CompareGreaterThanSigned, CilOperation.CompareGreaterThanUnsigned,
                CilOperation.CompareLessThanSigned, CilOperation.CompareLessThanUnsigned,
                CilOperation.AddChecked, CilOperation.AddCheckedUnsigned, CilOperation.SubtractChecked,
                CilOperation.SubtractCheckedUnsigned, CilOperation.MultiplyChecked, CilOperation.MultiplyCheckedUnsigned,
                CilOperation.Divide, CilOperation.DivideUnsigned, CilOperation.Remainder, CilOperation.RemainderUnsigned,
                CilOperation.LoadArrayElement, CilOperation.LoadArrayElementAddress], (2, 1)),
            ([CilOperation.Negate, CilOperation.OnesComplement, CilOperation.LoadField, CilOperation.LoadObject,
                CilOperation.LoadArrayLength,
                .. Enumerable.Range((int)CilOperation.ConvertInt32, CilOperation.ConvertNumeric - CilOperation.ConvertInt32 + 1)
                    .Select(static operation => (CilOperation)operation)], (1, 1)),
        }
        .SelectMany(static group => group.Operations.Select(operation => KeyValuePair.Create(operation, group.Shape)))
        .ToFrozenDictionary();
}
