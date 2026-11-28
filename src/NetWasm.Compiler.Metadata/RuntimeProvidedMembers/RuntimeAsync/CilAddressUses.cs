// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// What each address of a local or argument does, for liveness. An address that the instruction consuming it uses and
// drops (a call's receiver or by-reference argument, a field load or store, ldobj, stobj, cpobj, initobj) is read or
// written there and nowhere else, so the local is read at that point (written in full by initobj or a value type's
// constructor). Any other address may be read through anywhere: one stored, duplicated, branched with, offset by
// ldflda, or given to a call that returns a reference, a pointer or a by-reference-like value, or that takes or makes a
// by-reference-like value, which could keep it. Roslyn takes the address of most struct temporaries (an awaitable for
// ConfigureAwait, a Memory<T> it constructs in place, an awaiter for IsCompleted and GetResult), so without this every
// one of them would be live, and saved, at every suspension. Upstream's JIT reaches the same liveness by inlining those
// calls and promoting the struct before async.cpp's CreateLiveSetForSuspension asks AsyncAnalysis::IsLive.
internal sealed class CilAddressUses
{
    private readonly Dictionary<int, (bool Local, int Index)> _reads;
    private readonly Dictionary<int, (bool Local, int Index)> _writes;

    private CilAddressUses(BitArray exposedLocals, BitArray exposedArguments,
        Dictionary<int, (bool Local, int Index)> reads, Dictionary<int, (bool Local, int Index)> writes)
    {
        ExposedLocals = exposedLocals;
        ExposedArguments = exposedArguments;
        _reads = reads;
        _writes = writes;
    }

    // Locals and arguments with an address that may be read through anywhere.
    public BitArray ExposedLocals { get; }

    public BitArray ExposedArguments { get; }

    // The local or argument the instruction at the position reads through an address taken for it alone.
    internal bool Reads(int position, out (bool Local, int Index) target) => _reads.TryGetValue(position, out target);

    // The local or argument the instruction at the position writes in full through such an address.
    internal bool Writes(int position, out (bool Local, int Index) target) => _writes.TryGetValue(position, out target);

    public static CilAddressUses Compute(CilMethodBody body, int arguments, Func<CliTypeIdentity, bool> isByRefLike)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(isByRefLike);
        var instructions = body.Instructions;
        var entries = Entries(body);
        var exposedLocals = new BitArray(body.Locals.Length);
        var exposedArguments = new BitArray(arguments);
        var reads = new Dictionary<int, (bool Local, int Index)>();
        var writes = new Dictionary<int, (bool Local, int Index)>();
        for (var position = 0; position < instructions.Length; position++)
        {
            (bool Local, int Index) target;
            switch (instructions[position])
            {
                case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index local }:
                    target = (true, local.Value);
                    break;
                case { Operation: CilOperation.LoadArgumentAddress, Operand: CilOperand.Index argument }:
                    target = (false, argument.Value);
                    break;
                default:
                    continue;
            }
            var use = Consumer(instructions, position, target, entries, isByRefLike);
            if (use is (var consumer, var full))
            {
                if (full) writes[consumer] = target;
                else reads[position] = target;
            }
            else if (target.Local)
            {
                exposedLocals[target.Index] = true;
            }
            else
            {
                exposedArguments[target.Index] = true;
            }
        }
        return new(exposedLocals, exposedArguments, reads, writes);
    }

    // The instruction that consumes the address pushed at the position, and whether it writes the whole value, when it
    // uses the address there and drops it; null when the address may outlive it.
    private static (int Consumer, bool Full)? Consumer(IReadOnlyList<CilInstruction> instructions, int position,
        (bool Local, int Index) target, HashSet<int> entries, Func<CliTypeIdentity, bool> isByRefLike)
    {
        // Values on the stack from the address up: the address alone at first.
        var above = 1;
        for (var next = position + 1; next < instructions.Count; next++)
        {
            var instruction = instructions[next];
            // Code another path reaches, or a write or another address of the same value, before the consumer.
            if (entries.Contains(instruction.Offset) || Touches(instruction, target)) return null;
            if (CilStackDepths.Effect(instruction, int.MaxValue / 2) is not var (pops, pushes)) return null;
            if (pops >= above)
            {
                // The consumer: the address is its operand at this index, the first operand being 0.
                var operand = pops - above;
                return Uses(instruction, operand, isByRefLike) is { } full ? (next, full) : null;
            }
            if (instruction.Operand is CilOperand.BranchTarget or CilOperand.SwitchTargets ||
                instruction.Operation is CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
                    CilOperation.EndFinally or CilOperation.EndFilter)
            {
                return null;
            }
            above += pushes - pops;
        }
        return null;
    }

    // Whether the instruction uses the address at the operand index and drops it: false when it reads (or writes in
    // part), true when it writes the whole value, null when the address may outlive it.
    private static bool? Uses(CilInstruction instruction, int operand, Func<CliTypeIdentity, bool> isByRefLike) =>
        instruction.Operation switch
        {
            CilOperation.LoadField or CilOperation.LoadObject => false,
            CilOperation.StoreField or CilOperation.StoreObject when operand == 0 => false,
            CilOperation.CopyObject => false,
            CilOperation.InitializeObject => true,
            CilOperation.Call or CilOperation.CallVirtual or CilOperation.NewObject
                when instruction.Operand is CilOperand.MethodInstance { Value: var callee } &&
                    Keeps(callee, instruction.Operation, isByRefLike) is false =>
                instruction.Operation == CilOperation.Call && operand == 0 && !callee.Definition.IsStatic &&
                callee.DeclaringType.IsValueType && callee.Definition.Name == ".ctor",
            _ => null,
        };

    // Whether a call could keep an address it is given: by returning a reference, a pointer or a by-reference-like
    // value, by taking one it could store the address in, or by constructing a by-reference-like value.
    private static bool Keeps(MethodInstanceModel callee, CilOperation operation, Func<CliTypeIdentity, bool> isByRefLike)
    {
        var returned = Unmodified(callee.Signature.ReturnSignatureType);
        if (returned.Shape is CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or
                CliTypeShape.FunctionPointer || isByRefLike(returned))
            return true;
        if (operation == CilOperation.NewObject && isByRefLike(callee.DeclaringType)) return true;
        foreach (var parameter in callee.Signature.ParameterSignatureTypes)
        {
            var value = Unmodified(parameter);
            if (value.Shape == CliTypeShape.ManagedByReference && value.ElementType is { } element) value = Unmodified(element);
            if (isByRefLike(value)) return true;
        }
        return !callee.Definition.IsStatic && callee.DeclaringType.IsValueType && isByRefLike(callee.DeclaringType);
    }

    // The type under its custom modifiers (an in parameter's modreq, a volatile field's).
    private static CliTypeIdentity Unmodified(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified && type.ElementType is { } inner) type = inner;
        return type;
    }

    private static bool Touches(CilInstruction instruction, (bool Local, int Index) target) =>
        instruction is { Operand: CilOperand.Index { Value: var index } } && index == target.Index &&
        (target.Local
            ? instruction.Operation is CilOperation.StoreLocal or CilOperation.LoadLocalAddress
            : instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadArgumentAddress);

    // Offsets control reaches other than by falling through: branch and switch targets, and protected regions'
    // boundaries.
    private static HashSet<int> Entries(CilMethodBody body)
    {
        var entries = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget branch) entries.Add(branch.Offset);
            else if (instruction.Operand is CilOperand.SwitchTargets table) entries.UnionWith(table.Offsets);
        }
        foreach (var region in body.ExceptionRegions)
        {
            entries.Add(region.TryOffset);
            entries.Add(region.TryOffset + region.TryLength);
            entries.Add(region.HandlerOffset);
            entries.Add(region.HandlerOffset + region.HandlerLength);
            if (region.FilterOffset is { } filter) entries.Add(filter);
        }
        return entries;
    }
}
