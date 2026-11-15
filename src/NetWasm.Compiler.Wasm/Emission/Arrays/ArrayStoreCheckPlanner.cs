// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

// A reference store needs the covariant check only because an array can be
// typed by a base of its element type. Verified IL makes the value assignable
// to the static element type, so when that type is the array's actual element
// type the check cannot fail: the array came from newarr, or its static
// element type is a sealed class. RyuJIT skips the same check.
internal sealed class ArrayStoreCheckPlanner(
    IArgumentSignatureTypeResolver arguments,
    ITypeDefinitionResolver typeDefinitions) : IArrayStoreCheckPlanner
{
    private static readonly CliTypeIdentity String =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
    private readonly IArgumentSignatureTypeResolver _arguments = arguments ??
        throw new ArgumentNullException(nameof(arguments));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));

    public ImmutableHashSet<int> Plan(StructuredMethod method, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(method);
        var instructions = method.Header.Instructions;
        if (!enabled || !instructions.Any(static instruction =>
                instruction.Operation is CilOperation.StoreArrayElementReference or CilOperation.StoreArrayElement))
        {
            return [];
        }

        // Whether each stack slot holds an array whose element type is exact,
        // on every path to the instruction. Inlined bodies keep the array on
        // the stack across their branches, so the state follows control flow.
        var states = new bool[]?[instructions.Length];
        var indexes = new Dictionary<int, int>(instructions.Length);
        for (var index = 0; index < instructions.Length; index++)
        {
            indexes[instructions[index].Offset] = index;
        }
        var sealedClasses = new Dictionary<CliTypeIdentity, bool>();
        var pending = new Stack<int>();
        if (instructions.Length != 0)
        {
            states[0] = [];
            pending.Push(0);
        }
        while (pending.TryPop(out var index))
        {
            var instruction = instructions[index];
            var successors = Successors(instruction, index, indexes, instructions.Length);
            if (successors.Count == 0 ||
                !method.InstructionEntryStacks.TryGetValue(instructions[successors[0]].Offset, out var next))
            {
                continue;
            }
            var exact = new List<bool>(states[index]!);
            Transfer(method.Header, instruction, exact, next.Length, sealedClasses);
            foreach (var successor in successors)
            {
                if (!method.InstructionEntryStacks.TryGetValue(instructions[successor].Offset, out var stack) ||
                    stack.Length != exact.Count)
                {
                    continue;
                }
                if (states[successor] is not { } current)
                {
                    states[successor] = [.. exact];
                    pending.Push(successor);
                    continue;
                }
                var changed = false;
                for (var slot = 0; slot < current.Length; slot++)
                {
                    if (current[slot] && !exact[slot])
                    {
                        current[slot] = false;
                        changed = true;
                    }
                }
                if (changed)
                {
                    pending.Push(successor);
                }
            }
        }

        var stores = ImmutableHashSet.CreateBuilder<int>();
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operation is CilOperation.StoreArrayElementReference or CilOperation.StoreArrayElement &&
                states[index] is { Length: >= 3 } state && state[^3])
            {
                stores.Add(instructions[index].Offset);
            }
        }
        return stores.ToImmutable();
    }

    private void Transfer(
        StructuredMethodHeader header,
        CilInstruction instruction,
        List<bool> exact,
        int after,
        Dictionary<CliTypeIdentity, bool> sealedClasses)
    {
        if (instruction.Operation == CilOperation.Duplicate && exact.Count != 0)
        {
            exact.Add(exact[^1]);
            return;
        }
        // Every other instruction pushes at most one value, so the slots below
        // the next stack's top are ones it left untouched.
        var kept = Math.Min(exact.Count, Math.Max(after - 1, 0));
        exact.RemoveRange(kept, exact.Count - kept);
        while (exact.Count < after)
        {
            exact.Add(false);
        }
        if (exact.Count != 0 && Produces(header, instruction, sealedClasses))
        {
            exact[^1] = true;
        }
    }

    private static List<int> Successors(
        CilInstruction instruction,
        int index,
        Dictionary<int, int> indexes,
        int count)
    {
        var successors = new List<int>(2);
        if (instruction.Operation is not (CilOperation.Branch or CilOperation.Leave or CilOperation.Return or
                CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or CilOperation.EndFilter) &&
            index + 1 < count)
        {
            successors.Add(index + 1);
        }
        // Switches are lowered to branches before structuring.
        if (instruction.Operand is CilOperand.BranchTarget target)
        {
            successors.Add(indexes[target.Offset]);
        }
        return successors;
    }

    // Whether the instruction pushes an array whose element type is exact.
    private bool Produces(
        StructuredMethodHeader header,
        CilInstruction instruction,
        Dictionary<CliTypeIdentity, bool> sealedClasses) => instruction.Operation switch
        {
            CilOperation.NewArray => true,
            CilOperation.LoadLocal => HasExactElement(
                header.LocalSignatureTypes[CilOperandReader.GetIndex(instruction)], sealedClasses),
            CilOperation.LoadArgument => HasExactElement(
                _arguments.Resolve(header, CilOperandReader.GetIndex(instruction)), sealedClasses),
            CilOperation.LoadField or CilOperation.LoadStaticField =>
                instruction.Operand is CilOperand.FieldInstance field &&
                HasExactElement(field.Value.FieldType, sealedClasses),
            _ => false,
        };

    private bool HasExactElement(CliTypeIdentity type, Dictionary<CliTypeIdentity, bool> sealedClasses)
    {
        if (type.Shape != CliTypeShape.SzArray || type.ElementType is not { IsValueType: false } element)
        {
            return false;
        }
        if (element.Equals(String))
        {
            return true;
        }
        if (element.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
            element.ContainsGenericParameters)
        {
            return false;
        }
        if (!sealedClasses.TryGetValue(element, out var exact))
        {
            var definition = _typeDefinitions.ResolveTypeIdentity(element);
            exact = definition.IsSealed && !definition.IsInterface;
            sealedClasses.Add(element, exact);
        }
        return exact;
    }
}
