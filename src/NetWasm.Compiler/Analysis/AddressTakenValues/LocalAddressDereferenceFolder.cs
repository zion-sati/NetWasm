using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface ILocalAddressDereferenceFolder
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A primitive, enum or vector local whose address is read or written through
// in the same block, as an inlined receiver, out parameter or defensive copy
// leaves it, is accessed as the local: ldloca followed by a load of the local's
// own type, or of its primitive's value field, becomes ldloc, and a store or
// initobj through it becomes stloc. A Vector128<T> or Vector<T> local is read
// or written as any 16-byte vector type, as Unsafe.As views it. Once no address of a local remains it is no
// longer address-taken, so it needs no value-frame storage, and a method left
// with no such storage needs no frame. Runs after inlining, which turns those
// receivers and parameters into ldloca of the caller's local; an address-taken
// local's ldloc and stloc use the same storage as its address, so a partial fold
// changes nothing else.
internal sealed class LocalAddressDereferenceFolder(
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    bool enabled) : ILocalAddressDereferenceFolder
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly ITypedStackValidator _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || !HasCandidate(body)) return body;

        var entryStacks = _stacks.Validate(_graphs.Build(body)).InstructionEntryStacks;
        var producers = new StackSlotProducers(body, entryStacks);
        var instructions = body.Instructions;
        var rewritten = instructions.ToBuilder();
        var changed = false;
        // A compound assignment reads and writes through a dup of the address.
        var duplicated = new Dictionary<int, List<int>>();
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            // A prefixed access keeps its memory semantics.
            if (index > 0 && instructions[index - 1].Operation is CilOperation.Volatile or CilOperation.Unaligned ||
                !entryStacks.TryGetValue(instruction.Offset, out var stack))
            {
                continue;
            }
            var slot = instruction.Operation switch
            {
                CilOperation.LoadObject or CilOperation.LoadField or CilOperation.InitializeObject => stack.Length - 1,
                CilOperation.StoreObject or CilOperation.StoreField => stack.Length - 2,
                _ => -1,
            };
            if (slot < 0 ||
                producers.Find(index, slot, duplicates: true) is not { } found ||
                (instructions[found].Operation == CilOperation.Duplicate ? producers.FindTop(found) : found) is not { } producer ||
                instructions[producer] is not
                {
                    Operation: CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index { Value: var local },
                } ||
                (uint)local >= (uint)body.LocalSignatureTypes.Length ||
                !Accesses(instruction, body.LocalSignatureTypes[local]))
            {
                continue;
            }
            if (found != producer)
            {
                if (instruction.Operation != CilOperation.InitializeObject)
                {
                    (duplicated.TryGetValue(found, out var uses) ? uses : duplicated[found] = []).Add(index);
                }
                continue;
            }

            var type = body.LocalSignatureTypes[local];
            if (instruction.Operation == CilOperation.InitializeObject)
            {
                // initobj stores the zero of the local's type.
                if (Zero(type) is not { } zero) continue;
                rewritten[producer] = instructions[producer] with { Operation = zero.Operation, Operand = zero.Operand };
            }
            else
            {
                rewritten[producer] = instructions[producer] with
                {
                    Operation = CilOperation.Nop,
                    Operand = new CilOperand.None(),
                };
            }
            rewritten[index] = instruction with
            {
                Operation = instruction.Operation is CilOperation.LoadObject or CilOperation.LoadField
                    ? CilOperation.LoadLocal
                    : CilOperation.StoreLocal,
                Operand = new CilOperand.Index(local),
            };
            changed = true;
        }
        // Both copies must become the local's load or store, or the other
        // would lose its address.
        foreach (var (duplicate, uses) in duplicated)
        {
            if (uses.Count != 2) continue;
            var address = producers.FindTop(duplicate)!.Value;
            var local = ((CilOperand.Index)instructions[address].Operand).Value;
            rewritten[address] = instructions[address] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            rewritten[duplicate] = instructions[duplicate] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            foreach (var use in uses)
            {
                rewritten[use] = instructions[use] with
                {
                    Operation = instructions[use].Operation is CilOperation.LoadObject or CilOperation.LoadField
                        ? CilOperation.LoadLocal
                        : CilOperation.StoreLocal,
                    Operand = new CilOperand.Index(local),
                };
            }
            changed = true;
        }
        return changed ? body with { Instructions = rewritten.ToImmutable() } : body;
    }

    private static bool HasCandidate(CilMethodBody body)
    {
        foreach (var instruction in body.Instructions)
        {
            if (instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } } &&
                (uint)local < (uint)body.LocalSignatureTypes.Length &&
                (Width(body.LocalSignatureTypes[local]) is not null || IsVector(body.LocalSignatureTypes[local])))
            {
                return true;
            }
        }
        return false;
    }

    // Whether the access reads or writes exactly the local's value: an indirect
    // access or initobj of a type as wide as the local's, or its primitive's
    // or enum's value field. A narrow value is only read or cleared: a store
    // through the address truncates it and a local store may not.
    private static bool Accesses(CilInstruction instruction, CliTypeIdentity local)
    {
        if (IsVector(local))
        {
            return instruction.Operand is CilOperand.TypeIdentity { Value: var vector } && IsVector(vector);
        }
        return Width(local) is { } width &&
            (!Narrow(width) || instruction.Operation is not (CilOperation.StoreObject or CilOperation.StoreField)) &&
            instruction.Operand switch
            {
                CilOperand.TypeIdentity { Value: var type } => Width(type) == width,
                CilOperand.FieldInstance { Value: var field } =>
                    field.DeclaringType.Equals(local) && Width(field.FieldType) == width,
                _ => false,
            };
    }

    // The primitives by how they are stored and loaded; an enum is stored as
    // its underlying type.
    internal static string? Width(CliTypeIdentity type) =>
        (type is { Shape: CliTypeShape.Named, StackStorageType: { } storage } ? storage : type) is not
        {
            Shape: CliTypeShape.Primitive,
        } primitive
            ? null
            : primitive.CanonicalName switch
            {
                "primitive:i4" or "primitive:u4" => "i4",
                "primitive:i8" or "primitive:u8" => "i8",
                "primitive:f4" => "f4",
                "primitive:f8" => "f8",
                "primitive:nativeint" or "primitive:nativeuint" => "native",
                "primitive:u1" or "primitive:bool" => "u1",
                "primitive:i1" => "i1",
                "primitive:u2" or "primitive:char" => "u2",
                "primitive:i2" => "i2",
                _ => null,
            };

    private static bool Narrow(string width) => width is "u1" or "i1" or "u2" or "i2";

    private static bool IsVector(CliTypeIdentity type) => type.StackKind == CliValueKind.V128;

    private static (CilOperation Operation, CilOperand Operand)? Zero(CliTypeIdentity type) =>
        IsVector(type) ? (CilOperation.DefaultValue, new CilOperand.TypeIdentity(type)) : Width(type) switch
        {
            "i4" or "u1" or "i1" or "u2" or "i2" => (CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            "i8" => (CilOperation.LoadInt64, new CilOperand.ConstantI8(0)),
            "f4" => (CilOperation.LoadFloat32, new CilOperand.ConstantF4(0)),
            "f8" => (CilOperation.LoadFloat64, new CilOperand.ConstantF8(0)),
            _ => null,
        };
}
