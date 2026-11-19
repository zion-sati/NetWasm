// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public interface IYoungObjectStoreFinder
{
    // The IL offsets of the method's stores a collector with generations need not be told of: into an object it
    // allocated with no safepoint since, or of null.
    ImmutableHashSet<int> Find(ManagedMethodBody method, IReadOnlyDictionary<int, SafepointRootMap> safepoints);
}

// A collection happens only at a safepoint, so an object the method allocated is young until the next one it
// passes, and a minor collection traces every young object it reaches: a reference stored into the object then
// needs no card. The finder follows each block's evaluation stack and locals from an allocation to the stores that
// take the object as theirs: a field store (stfld) or an element store into the array. A newobj gives a young
// object when its constructor cannot collect, newarr always, and so does this in a constructor whose this is young
// when it starts (CilMethodBody.YoungThisAtEntry), in each block that every path from the start reaches without a
// safepoint, until the block's first. A safepoint ages every object followed; an
// instruction whose stack effect only the method it names decides forgets the stack, though not the locals, as it
// collects nothing; a block starts knowing nothing; a local whose address is taken is never followed. A store of
// null, which ldnull pushed, needs no barrier wherever it stores: it makes no reference to a young object.
public sealed class YoungObjectStoreFinder : IYoungObjectStoreFinder
{
    // What the finder knows of a value on the stack.
    private enum Value
    {
        Other,
        Young,
        Null,
    }

    public ImmutableHashSet<int> Find(ManagedMethodBody method, IReadOnlyDictionary<int, SafepointRootMap> safepoints)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(safepoints);
        var exposed = new HashSet<int>();
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operation == CilOperation.LoadLocalAddress) exposed.Add(((CilOperand.Index)instruction.Operand).Value);
        }
        var stores = ImmutableHashSet.CreateBuilder<int>();
        var stack = new List<Value>();
        var youngLocals = new HashSet<int>();
        var youngThis = YoungThisBlocks(method, safepoints);
        foreach (var block in method.ControlFlow.Graph.Blocks)
        {
            var instructions = block.Instructions;
            if (instructions.IsDefaultOrEmpty ||
                !method.ControlFlow.InstructionEntryStacks.TryGetValue(instructions[0].Offset, out var entry)) continue;
            Forget(stack, entry.Length);
            youngLocals.Clear();
            var thisYoung = youngThis is not null && youngThis[block.Index];
            for (var index = 0; index < instructions.Length; index++)
            {
                var instruction = instructions[index];
                var safepoint = safepoints.TryGetValue(instruction.Offset, out var map);
                if (safepoint)
                {
                    Age(stack);
                    youngLocals.Clear();
                    thisYoung = false;
                }
                if (NeedsNoRecord(instruction, stack)) stores.Add(instruction.Offset);
                var next = index + 1 < instructions.Length &&
                    method.ControlFlow.InstructionEntryStacks.TryGetValue(instructions[index + 1].Offset, out var after)
                        ? after
                        : default;
                if (thisYoung && instruction.Operation == CilOperation.LoadArgument &&
                    ((CilOperand.Index)instruction.Operand).Value == 0)
                {
                    stack.Add(Value.Young);
                    if (next.IsDefault) break;
                    if (stack.Count != next.Length) Forget(stack, next.Length);
                    continue;
                }
                if (!Follow(instruction, stack, youngLocals, exposed, map, next)) break;
            }
        }
        return stores.ToImmutable();
    }

    // For a constructor whose this is young when it starts, whether each block starts with it still young: the
    // first block does, and any other does when every block that leads to it ends with it young, which a block with
    // a safepoint does not. With exception regions only the first does: a handler starts after what it handles may
    // have collected, and a finally runs on a leave that the graph takes straight to its target. Null for any other
    // method, and for one that stores to this or takes its address.
    private static bool[]? YoungThisBlocks(ManagedMethodBody method, IReadOnlyDictionary<int, SafepointRootMap> safepoints)
    {
        if (!method.Body.YoungThisAtEntry) return null;
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operation is CilOperation.StoreArgument or CilOperation.LoadArgumentAddress &&
                ((CilOperand.Index)instruction.Operand).Value == 0) return null;
        }
        var graph = method.ControlFlow.Graph;
        var count = graph.Blocks.Length;
        if (!method.Body.ExceptionRegions.IsEmpty)
        {
            var entryOnly = new bool[count];
            entryOnly[graph.Entry.Index] = true;
            return entryOnly;
        }
        var predecessors = new List<int>[count];
        var collects = new bool[count];
        for (var block = 0; block < count; block++) predecessors[block] = [];
        for (var block = 0; block < count; block++)
        {
            foreach (var successor in graph.Successors[block]) predecessors[successor].Add(block);
            foreach (var instruction in graph.Blocks[block].Instructions)
            {
                if (safepoints.ContainsKey(instruction.Offset)) collects[block] = true;
            }
        }
        var young = new bool[count];
        Array.Fill(young, true);
        for (var changed = true; changed;)
        {
            changed = false;
            for (var block = 0; block < count; block++)
            {
                var value = block == graph.Entry.Index || predecessors[block].Count != 0 &&
                    predecessors[block].TrueForAll(predecessor => young[predecessor] && !collects[predecessor]);
                if (value == young[block]) continue;
                young[block] = value;
                changed = true;
            }
        }
        return young;
    }

    private static bool NeedsNoRecord(CilInstruction instruction, List<Value> stack) => instruction.Operation switch
    {
        CilOperation.StoreField => stack.Count >= 2 && (stack[^2] == Value.Young || stack[^1] == Value.Null),
        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference =>
            stack.Count >= 3 && (stack[^3] == Value.Young || stack[^1] == Value.Null),
        CilOperation.StoreObject or CilOperation.StoreRectangularArrayElement => stack.Count >= 2 && stack[^1] == Value.Null,
        _ => false,
    };

    // Applies the instruction to what is followed, given the stack the next instruction starts with (default at
    // the end of the block). False when the block's instructions cannot be followed past it.
    private static bool Follow(CilInstruction instruction, List<Value> stack, HashSet<int> youngLocals,
        HashSet<int> exposed, SafepointRootMap? map, ImmutableArray<CliValueKind> next)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal:
                stack.Add(youngLocals.Contains(((CilOperand.Index)instruction.Operand).Value) ? Value.Young : Value.Other);
                break;
            case CilOperation.LoadNull:
                stack.Add(Value.Null);
                break;
            case CilOperation.StoreLocal when stack.Count != 0:
                var local = ((CilOperand.Index)instruction.Operand).Value;
                if (stack[^1] == Value.Young && !exposed.Contains(local)) youngLocals.Add(local);
                else youngLocals.Remove(local);
                stack.RemoveAt(stack.Count - 1);
                break;
            case CilOperation.Duplicate when stack.Count != 0:
                stack.Add(stack[^1]);
                break;
            case CilOperation.NewArray when stack.Count != 0:
                stack[^1] = Value.Young;
                break;
            case CilOperation.NewObject:
                if (next.IsDefault) return false;
                var pops = stack.Count + 1 - next.Length;
                if (pops < 0 || pops > stack.Count) return Resynchronize(stack, next);
                stack.RemoveRange(stack.Count - pops, pops);
                stack.Add(map is { ConstructorCallRoots.IsDefaultOrEmpty: true } &&
                    next[^1] == CliValueKind.ManagedReference ? Value.Young : Value.Other);
                break;
            default:
                if (CilStackEffects.Of(instruction) is not { } effect || effect.Pops > stack.Count)
                    return Resynchronize(stack, next);
                stack.RemoveRange(stack.Count - effect.Pops, effect.Pops);
                for (var push = 0; push < effect.Pushes; push++) stack.Add(Value.Other);
                break;
        }
        return next.IsDefault || stack.Count == next.Length || Resynchronize(stack, next);
    }

    // Forgets the stack, which the next instruction starts with at its own depth; false at the end of the block.
    private static bool Resynchronize(List<Value> stack, ImmutableArray<CliValueKind> next)
    {
        if (next.IsDefault) return false;
        Forget(stack, next.Length);
        return true;
    }

    // A collection may age every young object followed; null stays null.
    private static void Age(List<Value> stack)
    {
        for (var slot = 0; slot < stack.Count; slot++)
        {
            if (stack[slot] == Value.Young) stack[slot] = Value.Other;
        }
    }

    private static void Forget(List<Value> stack, int depth)
    {
        stack.Clear();
        for (var slot = 0; slot < depth; slot++) stack.Add(Value.Other);
    }
}
