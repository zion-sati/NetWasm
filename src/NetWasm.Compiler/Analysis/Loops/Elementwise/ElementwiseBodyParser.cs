using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseCil;
using static NetWasm.Compiler.Analysis.Loops.Elementwise.ElementwiseNodes;

namespace NetWasm.Compiler.Analysis.Loops.Elementwise;

internal sealed class ElementwiseBodyParser(ICalledMethodResolver calledMethods) : IElementwiseBodyParser
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    // The element stores, folds, counter copies, element copies and span index
    // checks, from the body's instructions.
    public ElementwiseBody? Parse(CilInstruction[] body, int counter, HashSet<int> written, int reference = -1)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(written);
        var checks = new List<Check>();
        var stack = new List<ElementwiseNode>();
        var stores = new List<Store>();
        var folds = new List<Fold>();
        var aliases = new Dictionary<int, Linear>();
        var copies = new Dictionary<int, Element>();
        var accumulated = new HashSet<int>();
        ElementwiseNode Pop()
        {
            var node = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return node;
        }

        for (var position = 0; position < body.Length; position++)
        {
            var instruction = body[position];
            switch (instruction.Operation)
            {
                case CilOperation.Nop:
                    break;
                // A span indexer's check of the counter plus a constant around its throw.
                case CilOperation.BranchIfLessThanUnsigned when instruction.Operand is CilOperand.BranchTarget { Offset: var target }:
                    {
                        if (stack.Count < 2 || Pop() is not Invariant { Code: [var length] } ||
                            Pop() is not Linear { Scale: 1, Offset: var offset })
                        {
                            return null;
                        }
                        var resume = position + 1;
                        while (resume < body.Length && body[resume].Offset != target) resume++;
                        if (resume == body.Length || !Throws(body.AsSpan(position + 1, resume - position - 1))) return null;
                        checks.Add(new Check(offset, length));
                        position = resume - 1;
                        break;
                    }
                case CilOperation.ConvertNativeUInt or CilOperation.ConvertNativeInt:
                    if (stack.Count < 1 || Pop() is not Linear { Scale: 1, Offset: var counterOffset }) return null;
                    stack.Add(new NativeIndex(counterOffset, Copy(instruction)));
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    if (local == counter)
                    {
                        stack.Add(new Linear(1, 0));
                    }
                    else if (local == reference)
                    {
                        stack.Add(new Reference(instruction));
                    }
                    else if (aliases.TryGetValue(local, out var alias))
                    {
                        stack.Add(alias);
                    }
                    else if (copies.TryGetValue(local, out var copied))
                    {
                        stack.Add(copied);
                    }
                    else if (written.Contains(local))
                    {
                        // Read before this iteration writes it: the last iteration's
                        // value, which only the local's fold may read.
                        accumulated.Add(local);
                        stack.Add(new Accumulator(local));
                    }
                    else
                    {
                        stack.Add(new Invariant([Copy(instruction)], CliValueKind.Unknown));
                    }
                    break;
                case CilOperation.LoadArgument:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.Unknown));
                    break;
                case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 { Value: var value }:
                    stack.Add(new Linear(0, value));
                    break;
                case CilOperation.LoadInt64:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.I8));
                    break;
                case CilOperation.LoadFloat32:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.F4));
                    break;
                case CilOperation.LoadFloat64:
                    stack.Add(new Invariant([Copy(instruction)], CliValueKind.F8));
                    break;
                case CilOperation.LoadArrayElement when instruction.Operand is CilOperand.TypeIdentity { Value: var loaded }:
                    {
                        if (stack.Count < 2 || Pop() is not Linear { Scale: 1, Offset: var offset } ||
                            Pop() is not Invariant { Code: [var array] } || !IsArray(array) ||
                            Lane(loaded) == CliValueKind.Unknown || Math.Abs(offset) >= MaximumOffset)
                        {
                            return null;
                        }
                        stack.Add(new Element(array, offset, loaded));
                        break;
                    }
                // *p, where p walks the elements, or a span's element.
                case CilOperation.LoadObject when instruction.Operand is CilOperand.TypeIdentity { Value: var loaded }:
                    if (stack.Count < 1 || Lane(loaded) == CliValueKind.Unknown) return null;
                    switch (Pop())
                    {
                        case Reference { Load: var load }:
                            stack.Add(new Element(load, 0, loaded, ByReference: true));
                            break;
                        case SpanAddress address when address.Type.CanonicalName == loaded.CanonicalName:
                            stack.Add(new Element(address.Reference, address.Offset, loaded, Span: address.Indexer));
                            break;
                        default:
                            return null;
                    }
                    break;
                case CilOperation.StoreArrayElement when instruction.Operand is CilOperand.TypeIdentity { Value: var stored }:
                    {
                        if (stack.Count < 3) return null;
                        var value = Pop();
                        if (Pop() is not Linear { Scale: 1, Offset: var offset } ||
                            Pop() is not Invariant { Code: [var array] } || !IsArray(array) ||
                            Lane(stored) == CliValueKind.Unknown || Math.Abs(offset) >= MaximumOffset)
                        {
                            return null;
                        }
                        stores.Add(new Store(array, offset, stored, value));
                        break;
                    }
                case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index { Value: var local }:
                    {
                        if (stack.Count < 1 || local == counter) return null;
                        var value = Pop();
                        var fresh = !folds.Any(fold => fold.Local == local) && !accumulated.Contains(local) &&
                            !aliases.ContainsKey(local) && !copies.ContainsKey(local);
                        // k = i + c, which later index and value expressions read.
                        if (value is Linear { Scale: not 0 } copy && fresh)
                        {
                            aliases[local] = copy;
                            break;
                        }
                        // s = a[i + c], as foreach copies an element.
                        if (value is Element element && fresh)
                        {
                            copies[local] = element;
                            break;
                        }
                        // x = x op value, once an iteration.
                        if (value is Binary
                            {
                                Operation: CilOperation.Add or CilOperation.BitwiseOr or CilOperation.BitwiseXor or
                                CilOperation.BitwiseAnd
                            } fold &&
                            (fold.Left, fold.Right) switch
                            {
                                (Accumulator { Local: var left }, var folded) when left == local => folded,
                                (var folded, Accumulator { Local: var right }) when right == local => folded,
                                _ => null,
                            } is { } operand &&
                            !Accumulators(operand).Any() && !aliases.ContainsKey(local) && !folds.Any(other => other.Local == local))
                        {
                            folds.Add(new Fold(local, fold.Operation, operand));
                            break;
                        }
                        return null;
                    }
                case CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or
                    CilOperation.BitwiseAnd or CilOperation.BitwiseOr or CilOperation.BitwiseXor:
                    {
                        if (stack.Count < 2) return null;
                        var right = Pop();
                        if (Combine(instruction.Operation, Pop(), right) is not { } combined) return null;
                        stack.Add(combined);
                        break;
                    }
                case CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned:
                    {
                        if (stack.Count < 2 || Pop() is not Linear { Scale: 0, Offset: var count }) return null;
                        var value = Pop();
                        stack.Add(value is Invariant invariant
                            ? Hoisted(invariant, Instruction(CilOperation.LoadInt32, new CilOperand.ConstantI4(count)), Copy(instruction))
                            : new Shift(instruction.Operation, value, count));
                        break;
                    }
                case CilOperation.Negate or CilOperation.OnesComplement:
                    {
                        if (stack.Count < 1) return null;
                        var operand = Pop();
                        stack.Add(operand switch
                        {
                            Linear { Scale: 0, Offset: var value } => new Linear(0, instruction.Operation == CilOperation.Negate
                                ? unchecked(-value) : ~value),
                            Invariant invariant => Hoisted(invariant, Copy(instruction)),
                            _ => new Unary(instruction.Operation, operand),
                        });
                        break;
                    }
                case CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertInt64:
                    {
                        // Only of values the loop does not change.
                        if (stack.Count < 1 || AsInvariant(Pop()) is not { } invariant) return null;
                        stack.Add(new Invariant([.. invariant.Code, Copy(instruction)], instruction.Operation switch
                        {
                            CilOperation.ConvertFloat32 => CliValueKind.F4,
                            CilOperation.ConvertFloat64 => CliValueKind.F8,
                            _ => CliValueKind.I8,
                        }));
                        break;
                    }
                // Unsafe.Add(ref reference, index), index the counter plus a constant.
                case CilOperation.Call when _calledMethods.Resolve(instruction) is { } add && IsUnsafeAdd(add):
                    {
                        if (stack.Count < 2) return null;
                        var (elementOffset, conversion) = Pop() switch
                        {
                            NativeIndex native when add.Signature.ParameterTypes[1] == CliValueKind.NativeInt =>
                                ((int?)native.Offset, native.Conversion),
                            Linear { Scale: 1 } linear when add.Signature.ParameterTypes[1] == CliValueKind.I4 => (linear.Offset, null),
                            _ => (null, (CilInstruction?)null),
                        };
                        if (elementOffset is not { } offset || Pop() is not Invariant { Code: [var span] })
                        {
                            return null;
                        }
                        stack.Add(new SpanAddress(span, offset, add.MethodArguments[0], new SpanIndexer(Copy(instruction), conversion)));
                        break;
                    }
                case CilOperation.Call:
                    {
                        if (_calledMethods.Resolve(instruction) is not { } target || !LoopInvariantCallHoister.IsPure(target)) return null;
                        var count = target.Signature.ParameterTypes.Length;
                        if (stack.Count < count) return null;
                        var arguments = new List<Invariant>();
                        for (var index = 0; index < count; index++)
                        {
                            if (AsInvariant(Pop()) is not { } argument) return null;
                            arguments.Insert(0, argument);
                        }
                        stack.Add(new Invariant([.. arguments.SelectMany(static argument => argument.Code), Copy(instruction)],
                            target.Signature.ReturnType));
                        break;
                    }
                default:
                    return null;
            }
        }
        // A local's earlier value reaches nothing but its fold, and the walking
        // reference nothing but element loads.
        return stack.Count == 0 && !stores.Any(static store => Accumulators(store.Value).Any()) &&
            !stores.Select(static store => store.Value).Concat(folds.Select(static fold => fold.Value)).Any(static value => References(value))
            ? new ElementwiseBody(stores, folds, aliases, copies, checks)
            : null;
    }

    // A block that only throws: a call that takes nothing, then optionally null, then throw.
    private bool Throws(ReadOnlySpan<CilInstruction> block)
    {
        var index = 0;
        while (index < block.Length && block[index].Operation == CilOperation.Nop) index++;
        if (index == block.Length || block[index].Operation != CilOperation.Call ||
            _calledMethods.Resolve(block[index]) is not { } thrower || thrower.Signature.ParameterTypes.Length != 0)
        {
            return false;
        }
        index++;
        if (index < block.Length && block[index].Operation == CilOperation.LoadNull) index++;
        return index == block.Length - 1 && block[index].Operation == CilOperation.Throw;
    }

    // Unsafe.Add<T>(ref T, int), (ref T, nint) or (ref T, nuint).
    private static bool IsUnsafeAdd(MethodInstanceModel method) => method is
    {
        DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
        Definition.Name: "Add",
        MethodArguments: [_],
        Signature.ParameterTypes: [CliValueKind.ManagedAddress, CliValueKind.I4 or CliValueKind.NativeInt],
    };
}
