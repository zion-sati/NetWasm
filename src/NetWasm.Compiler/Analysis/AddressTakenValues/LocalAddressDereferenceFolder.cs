using System;
using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.AddressTakenValues;

internal interface ILocalAddressDereferenceFolder
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A primitive, enum or vector local or argument whose address is read or
// written through in the same block, as an inlined receiver, out parameter or
// defensive copy leaves it, is accessed as the variable: ldloca or ldarga
// followed by a load of the variable's own type, or of its primitive's value
// field, becomes ldloc or ldarg, and a store or initobj through it becomes
// stloc or starg. A vector local is read or written as any
// vector type of its width, as Unsafe.As views it, and its field read just
// after its address reads the local's lane. Once no address of a local remains it is no
// longer address-taken, so it needs no value-frame storage, and a method left
// with no such storage needs no frame. Runs after inlining, which turns those
// receivers and parameters into ldloca or ldarga of the caller's variable; an
// address-taken variable's loads and stores use the same storage as its address,
// so a partial fold changes nothing else.
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
                Variable(body, instructions[producer]) is not { } variable ||
                !Accesses(instruction, variable.Type))
            {
                continue;
            }
            if (instruction.Operation == CilOperation.LoadField && IsVector(variable.Type))
            {
                // A vector local's field reads its lane from the local's value,
                // which nothing can change between the address and the read
                // just after it.
                if (found == producer && producer == index - 1)
                {
                    rewritten[producer] = instructions[producer] with { Operation = variable.Load };
                    changed = true;
                }
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

            if (instruction.Operation == CilOperation.InitializeObject)
            {
                // initobj stores the zero of the variable's type.
                if (Zero(variable.Type) is not { } zero) continue;
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
                    ? variable.Load
                    : variable.Store,
                Operand = new CilOperand.Index(variable.Index),
            };
            changed = true;
        }
        // Both copies must become the local's load or store, or the other
        // would lose its address.
        foreach (var (duplicate, uses) in duplicated)
        {
            if (uses.Count != 2) continue;
            var address = producers.FindTop(duplicate)!.Value;
            var variable = Variable(body, instructions[address])!.Value;
            rewritten[address] = instructions[address] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            rewritten[duplicate] = instructions[duplicate] with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            foreach (var use in uses)
            {
                rewritten[use] = instructions[use] with
                {
                    Operation = instructions[use].Operation is CilOperation.LoadObject or CilOperation.LoadField
                        ? variable.Load
                        : variable.Store,
                    Operand = new CilOperand.Index(variable.Index),
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
            if (Variable(body, instruction) is { } variable &&
                (Width(variable.Type) is not null || IsVector(variable.Type)))
            {
                return true;
            }
        }
        return false;
    }

    // The local or argument an ldloca or ldarga addresses, with its type and how it is loaded and
    // stored; an instance method's `this` is already an address, so its own address is left alone.
    private static (int Index, CliTypeIdentity Type, CilOperation Load, CilOperation Store)? Variable(
        CilMethodBody body, CilInstruction address)
    {
        switch (address)
        {
            case { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } }
                when (uint)local < (uint)body.LocalSignatureTypes.Length:
                return (local, body.LocalSignatureTypes[local], CilOperation.LoadLocal, CilOperation.StoreLocal);
            case { Operation: CilOperation.LoadArgumentAddress, Operand: CilOperand.Index { Value: var argument } }:
                var parameter = body.Method.IsStatic ? argument : argument - 1;
                var parameters = (body.MethodInstance?.Signature ?? body.Method.Signature).ParameterSignatureTypes;
                // A narrow argument holds what its caller passed, which a load through its address
                // would truncate, so only full-width arguments fold.
                return (uint)parameter < (uint)parameters.Length &&
                    (Width(parameters[parameter]) is not { } width || !Narrow(width))
                    ? (argument, parameters[parameter], CilOperation.LoadArgument, CilOperation.StoreArgument)
                    : null;
            default:
                return null;
        }
    }

    // Whether the access reads or writes exactly the local's value: an indirect
    // access or initobj of a type as wide as the local's, or its primitive's
    // or enum's value field. A narrow value is only read or cleared: a store
    // through the address truncates it and a local store may not.
    private static bool Accesses(CilInstruction instruction, CliTypeIdentity local)
    {
        if (IsVector(local))
        {
            // Only a view as wide as the local: a Vector2 written into a
            // Vector4's low half keeps the upper half.
            return instruction.Operand switch
            {
                CilOperand.TypeIdentity { Value: var vector } =>
                    IsVector(vector) && VectorWidth(vector) == VectorWidth(local),
                CilOperand.FieldInstance { Value: var field } =>
                    instruction.Operation == CilOperation.LoadField && field.DeclaringType.Equals(local),
                _ => false,
            };
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

    private static int VectorWidth(CliTypeIdentity type) => type.FullName switch
    {
        "System.Numerics.Vector2" => 8,
        "System.Numerics.Vector3" => 12,
        _ => 16,
    };

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
