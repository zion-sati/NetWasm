// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IBlockLocalSlotSharer
{
    // The body with the locals that never hold a value from one block into another sharing slots by type.
    CilMethodBody Rewrite(CilMethodBody body);
}

// Debug IL gives every condition, and many other temporaries, a local of its own, and each local is one of the
// function's locals. An engine that keeps the locals' state at every branch and merge (V8 does, in both its tiers)
// then compiles a method in memory proportional to its locals times its blocks: a 20,000-arm else-if chain, each
// condition with its own temporary, takes V8 gigabytes and aborts it. A local whose every read follows a write in
// the same block never carries a value from one block into another, so locals of one type that never appear in the
// same block can be one local, as Release IL reuses its temporaries. A local whose address is taken, or that the
// signature pins, keeps its own.
internal sealed class BlockLocalSlotSharer(IControlFlowGraphBuilder graphs, bool enabled) : IBlockLocalSlotSharer
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        // A proof names locals by their indices, so a body that carries one keeps them.
        if (!enabled || body.ArrayBoundsProof is not null || body.IntegerCheckProof is not null) return body;
        var candidates = Candidates(body);
        // Without two candidates of one type nothing can share, and the graph is not built.
        if (!HasTwoOfOneType(body, candidates)) return body;
        var blocks = BlocksByLocal(body, candidates);
        var slots = Slots(body, candidates, blocks);
        if (slots.Count == body.Locals.Length) return body;
        var instructions = body.Instructions.Select(instruction => instruction is
        {
            Operation: CilOperation.LoadLocal or CilOperation.StoreLocal or CilOperation.LoadLocalAddress,
            Operand: CilOperand.Index { Value: var local },
        }
            ? instruction with { Operand = new CilOperand.Index(slots.Index[local]) }
            : instruction);
        return body with
        {
            Instructions = [.. instructions],
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => region.PassThroughLocal is { } local
                ? region with { PassThroughLocal = slots.Index[local] }
                : region)],
            Locals = [.. slots.First.Select(local => body.Locals[local])],
            LocalSignatureTypes = [.. slots.First.Select(local => body.LocalSignatureTypes[local])],
            PinnedLocals = [.. body.PinnedLocals.Select(local => slots.Index[local])],
        };
    }

    // The locals that may share: neither pinned, address-taken nor read by a protected block's entry.
    private static bool[] Candidates(CilMethodBody body)
    {
        var candidates = new bool[body.Locals.Length];
        Array.Fill(candidates, true);
        foreach (var local in body.PinnedLocals)
        {
            candidates[local] = false;
        }
        foreach (var region in body.ExceptionRegions)
        {
            if (region.PassThroughLocal is { } local) candidates[local] = false;
        }
        foreach (var instruction in body.Instructions)
        {
            if (instruction is { Operation: CilOperation.LoadLocalAddress, Operand: CilOperand.Index { Value: var local } })
            {
                candidates[local] = false;
            }
        }
        return candidates;
    }

    private static bool HasTwoOfOneType(CilMethodBody body, bool[] candidates)
    {
        var seen = new HashSet<(CliValueKind, CliTypeIdentity)>();
        for (var local = 0; local < candidates.Length; local++)
        {
            if (candidates[local] && !seen.Add(Type(body, local))) return true;
        }
        return false;
    }

    // The blocks each candidate appears in. A candidate a block reads before it writes it holds a value into that
    // block (the method's zeroed locals included), so it stops being a candidate.
    private List<int>?[] BlocksByLocal(CilMethodBody body, bool[] candidates)
    {
        var blocks = new List<int>?[candidates.Length];
        var written = new HashSet<int>();
        foreach (var block in _graphs.Build(body).Blocks)
        {
            written.Clear();
            foreach (var instruction in block.Instructions)
            {
                if (instruction is not
                    {
                        Operation: CilOperation.LoadLocal or CilOperation.StoreLocal,
                        Operand: CilOperand.Index { Value: var local },
                    } || !candidates[local])
                {
                    continue;
                }
                if (instruction.Operation == CilOperation.LoadLocal && !written.Contains(local))
                {
                    candidates[local] = false;
                    continue;
                }
                written.Add(local);
                var appearances = blocks[local] ??= [];
                if (appearances.Count == 0 || appearances[^1] != block.Index)
                {
                    appearances.Add(block.Index);
                }
            }
        }
        return blocks;
    }

    // Each local's new index and, for each new index, the first local it holds. A candidate takes the lowest slot of
    // its type that no candidate in any of its blocks holds; every other local keeps a slot of its own.
    private static LocalSlots Slots(CilMethodBody body, bool[] candidates, List<int>?[] blocks)
    {
        var index = new int[candidates.Length];
        var first = new List<int>();
        var slotsByType = new Dictionary<(CliValueKind, CliTypeIdentity), List<int>>();
        var held = new HashSet<(int Block, int Slot)>();
        for (var local = 0; local < candidates.Length; local++)
        {
            if (!candidates[local])
            {
                index[local] = first.Count;
                first.Add(local);
                continue;
            }
            var type = Type(body, local);
            if (!slotsByType.TryGetValue(type, out var slots))
            {
                slots = [];
                slotsByType.Add(type, slots);
            }
            var appearances = blocks[local] ?? [];
            var slot = slots.FirstOrDefault(slot => appearances.All(block => !held.Contains((block, slot))), -1);
            if (slot < 0)
            {
                slot = first.Count;
                first.Add(local);
                slots.Add(slot);
            }
            foreach (var block in appearances)
            {
                held.Add((block, slot));
            }
            index[local] = slot;
        }
        return new(index, first);
    }

    private static (CliValueKind, CliTypeIdentity) Type(CilMethodBody body, int local) =>
        (body.Locals[local], body.LocalSignatureTypes[local]);

    private sealed record LocalSlots(int[] Index, List<int> First)
    {
        public int Count => First.Count;
    }
}
