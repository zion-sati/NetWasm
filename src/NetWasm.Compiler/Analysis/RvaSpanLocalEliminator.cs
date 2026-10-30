using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IRvaSpanLocalEliminator
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A ReadOnlySpan<T> local assigned once from RVA data, before every read of it,
// holds an address and a length known when compiling. Its reference field is a
// byref, so the local would live in the value frame; reading its fields as those
// constants instead leaves it unused. Runs after inlining, which turns the span's
// indexer and length into field reads of the local.
internal sealed class RvaSpanLocalEliminator(
    IControlFlowGraphBuilder graphs,
    IControlFlowDominanceAnalyzer dominance,
    bool enabled) : IRvaSpanLocalEliminator
{
    private readonly IControlFlowGraphBuilder _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly IControlFlowDominanceAnalyzer _dominance = dominance ??
        throw new ArgumentNullException(nameof(dominance));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var instructions = body.Instructions;
        var entries = EntryOffsets(body);
        var definitions = FindDefinitions(body, entries);
        if (definitions.Count == 0) return body;

        var uses = definitions.Keys.ToDictionary(local => local, _ => new List<int>());
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index] is not
                {
                    Operation: CilOperation.LoadLocal or CilOperation.LoadLocalAddress or CilOperation.StoreLocal,
                    Operand: CilOperand.Index { Value: var local },
                } instruction ||
                !definitions.TryGetValue(local, out var definition) ||
                index == definition.Store)
            {
                continue;
            }
            // The address is read only by a field load that follows it on the same path.
            var read = instruction.Operation == CilOperation.LoadLocal ||
                instruction.Operation == CilOperation.LoadLocalAddress &&
                index + 1 < instructions.Length &&
                !entries.Contains(instructions[index + 1].Offset) &&
                SpanField(instructions[index + 1], definition.View) is not null;
            if (read)
            {
                uses[local].Add(index);
            }
            else
            {
                definitions.Remove(local);
            }
        }
        if (definitions.Count == 0) return body;

        var graph = _graphs.Build(body);
        var blocks = graph.Blocks
            .SelectMany(block => block.Instructions.Select(instruction => (instruction.Offset, block.Index)))
            .ToDictionary(pair => pair.Offset, pair => pair.Index);
        var dominators = _dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var rewritten = instructions.ToArray();
        var changed = false;
        foreach (var (local, definition) in definitions)
        {
            var definingBlock = blocks[instructions[definition.Store].Offset];
            if (!uses[local].All(use => blocks[instructions[use].Offset] is var block &&
                    (block == definingBlock
                        ? use > definition.Store
                        : dominators.TryGetValue(block, out var blockDominators) &&
                            blockDominators.Contains(definingBlock))))
            {
                continue;
            }

            Clear(definition.Producer);
            Clear(definition.Store);
            foreach (var use in uses[local])
            {
                if (instructions[use].Operation == CilOperation.LoadLocal)
                {
                    rewritten[use] = instructions[use] with
                    {
                        Operation = CilOperation.CreateReadOnlySpanFromRva,
                        Operand = definition.View,
                    };
                    continue;
                }
                Clear(use);
                rewritten[use + 1] = SpanField(instructions[use + 1], definition.View) == "_length"
                    ? instructions[use + 1] with
                    {
                        Operation = CilOperation.LoadInt32,
                        Operand = new CilOperand.ConstantI4(definition.View.ElementCount),
                    }
                    : instructions[use + 1] with
                    {
                        Operation = CilOperation.LoadStaticFieldAddress,
                        Operand = new CilOperand.FieldInstance(definition.View.Field),
                    };
            }
            changed = true;
        }
        return changed ? body with { Instructions = [.. rewritten] } : body;

        void Clear(int index) => rewritten[index] = instructions[index] with
        {
            Operation = CilOperation.Nop,
            Operand = new CilOperand.None(),
        };
    }

    // A span assigned once, from RVA data, outside any protected region or handler,
    // so a handler never observes the local before its assignment.
    private static Dictionary<int, Definition> FindDefinitions(CilMethodBody body, HashSet<int> entries)
    {
        var instructions = body.Instructions;
        var stores = instructions
            .Where(static instruction => instruction is
            {
                Operation: CilOperation.StoreLocal,
                Operand: CilOperand.Index,
            })
            .GroupBy(static instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var definitions = new Dictionary<int, Definition>();
        for (var index = 1; index < instructions.Length; index++)
        {
            if (instructions[index - 1] is not
                {
                    Operation: CilOperation.CreateReadOnlySpanFromRva,
                    Operand: CilOperand.ReadOnlySpanData view,
                } ||
                instructions[index] is not
                {
                    Operation: CilOperation.StoreLocal,
                    Operand: CilOperand.Index { Value: var local },
                } store ||
                entries.Contains(store.Offset) ||
                stores[local] != 1 ||
                !body.LocalSignatureTypes[local].Equals(view.SpanType) ||
                body.ExceptionRegions.Any(region => Protects(region, store.Offset)))
            {
                continue;
            }
            definitions.Add(local, new(index - 1, index, view));
        }
        return definitions;
    }

    private static string? SpanField(CilInstruction instruction, CilOperand.ReadOnlySpanData view) =>
        instruction is { Operation: CilOperation.LoadField, Operand: CilOperand.FieldInstance { Value: var field } } &&
        field.DeclaringType.Equals(view.SpanType) &&
        field.Definition.Name is "_reference" or "_length"
            ? field.Definition.Name
            : null;

    private static bool Protects(CilExceptionRegion region, int offset) =>
        offset >= region.TryOffset && offset < region.TryOffset + region.TryLength ||
        offset >= region.HandlerOffset && offset < region.HandlerOffset + region.HandlerLength ||
        region.FilterOffset is int filter && offset >= filter && offset < region.HandlerOffset;

    private static HashSet<int> EntryOffsets(CilMethodBody body)
    {
        var offsets = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case CilOperand.BranchTarget branch:
                    offsets.Add(branch.Offset);
                    break;
                case CilOperand.SwitchTargets branches:
                    offsets.UnionWith(branches.Offsets);
                    break;
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            offsets.Add(region.TryOffset);
            offsets.Add(region.HandlerOffset);
            if (region.FilterOffset is int filter)
            {
                offsets.Add(filter);
            }
        }
        return offsets;
    }

    private sealed record Definition(int Producer, int Store, CilOperand.ReadOnlySpanData View);
}
