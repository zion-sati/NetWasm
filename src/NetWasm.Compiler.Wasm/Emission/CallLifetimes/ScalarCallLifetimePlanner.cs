using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal sealed class ScalarCallLifetimePlanner(
    IControlFlowGraphBuilder graphs,
    IControlFlowNaturalLoopAnalyzer loops) : IScalarCallLifetimePlanner
{
    private readonly IControlFlowGraphBuilder _graphs =
        graphs ?? throw new ArgumentNullException(nameof(graphs));
    private readonly IControlFlowNaturalLoopAnalyzer _loops =
        loops ?? throw new ArgumentNullException(nameof(loops));

    public ScalarCallLifetimePlan Plan(StructuredMethod method, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!enabled || !method.ExceptionGroups.IsEmpty)
        {
            return ScalarCallLifetimePlan.Empty;
        }

        var header = method.Header;
        // A try that constant branches leave unreachable has no exception group,
        // but its leave and handler instructions remain, and the graph below is
        // built without exception regions. Such a method keeps exact lifetimes,
        // as one with a reachable region does.
        if (header.Instructions.Any(instruction => instruction.Operation is
                CilOperation.Leave or CilOperation.EndFinally or CilOperation.EndFilter or
                CilOperation.Rethrow))
        {
            return ScalarCallLifetimePlan.Empty;
        }
        var candidates = header.LocalSignatureTypes
            .Select((type, index) => (type, index))
            // Preserve the numeric classes with measured cross-call spill
            // regressions. References and addresses retain the exact-root ABI.
            .Where(item => item.type.StackKind is CliValueKind.F4 or CliValueKind.F8 or CliValueKind.I8)
            .Select(item => item.index)
            .ToHashSet();
        foreach (var instruction in header.Instructions)
        {
            if (instruction.Operation == CilOperation.LoadLocalAddress)
            {
                candidates.Remove(CilOperandReader.GetIndex(instruction));
            }
        }
        if (candidates.Count == 0)
        {
            return ScalarCallLifetimePlan.Empty;
        }

        var graph = _graphs.Build(new CilMethodBody(
            header.Method, header.MaxStack, header.Locals, header.Instructions)
        {
            MethodInstance = header.MethodInstance,
            LocalSignatureTypes = header.LocalSignatureTypes,
        });
        var loopBlocks = _loops.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks)
            .SelectMany(loop => loop).ToImmutableHashSet();
        if (loopBlocks.IsEmpty)
        {
            return ScalarCallLifetimePlan.Empty;
        }

        var liveIn = EmptySets(graph);
        var liveOut = EmptySets(graph);
        bool changed;
        do
        {
            changed = false;
            foreach (var index in graph.ReachableBlocks.OrderDescending())
            {
                var live = new HashSet<int>();
                foreach (var successor in graph.Successors[index])
                {
                    live.UnionWith(liveIn[successor]);
                }
                changed |= Replace(liveOut, index, live);
                foreach (var instruction in graph.GetBlock(index).Instructions.Reverse())
                {
                    ApplyLiveness(instruction, candidates, live);
                }
                changed |= Replace(liveIn, index, live);
            }
        } while (changed);

        // Track whether a local's current value can originate in a loop. A
        // later assignment outside a loop kills that provenance. Union at a
        // join safely includes zero-trip paths without changing their values.
        var reaching = EmptySets(graph);
        do
        {
            changed = false;
            foreach (var index in graph.ReachableBlocks.Order())
            {
                var origins = Incoming(graph, reaching, index);
                foreach (var instruction in graph.GetBlock(index).Instructions)
                {
                    ApplyDefinition(instruction, candidates, origins, loopBlocks.Contains(index));
                }
                changed |= Replace(reaching, index, origins);
            }
        } while (changed);

        var calls = ImmutableDictionary.CreateBuilder<int, ImmutableArray<int>>();
        foreach (var index in graph.ReachableBlocks.Order())
        {
            if (loopBlocks.Contains(index))
            {
                continue;
            }
            var block = graph.GetBlock(index);
            var live = new HashSet<int>(liveOut[index]);
            var liveAtCalls = new Dictionary<int, HashSet<int>>();
            foreach (var instruction in block.Instructions.Reverse())
            {
                if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)
                {
                    liveAtCalls.Add(instruction.Offset, new(live));
                }
                ApplyLiveness(instruction, candidates, live);
            }
            var origins = Incoming(graph, reaching, index);
            foreach (var instruction in block.Instructions)
            {
                if (liveAtCalls.TryGetValue(instruction.Offset, out var after))
                {
                    var selected = origins.Where(after.Contains).Order().ToImmutableArray();
                    if (!selected.IsEmpty)
                    {
                        calls.Add(instruction.Offset, selected);
                    }
                }
                ApplyDefinition(instruction, candidates, origins, inLoop: false);
            }
        }
        return calls.Count == 0 ? ScalarCallLifetimePlan.Empty : new(calls.ToImmutable());
    }

    private static Dictionary<int, HashSet<int>> EmptySets(ControlFlowGraph graph) =>
        graph.ReachableBlocks.ToDictionary(index => index, _ => new HashSet<int>());

    private static HashSet<int> Incoming(
        ControlFlowGraph graph, Dictionary<int, HashSet<int>> outgoing, int index)
    {
        var incoming = new HashSet<int>();
        foreach (var predecessor in graph.Predecessors[index])
        {
            if (outgoing.TryGetValue(predecessor, out var values))
            {
                incoming.UnionWith(values);
            }
        }
        return incoming;
    }

    private static bool Replace(
        Dictionary<int, HashSet<int>> sets, int index, HashSet<int> values)
    {
        if (sets[index].SetEquals(values))
        {
            return false;
        }
        sets[index] = new(values);
        return true;
    }

    private static void ApplyLiveness(
        CilInstruction instruction, HashSet<int> candidates, HashSet<int> live)
    {
        if (instruction.Operation is not (CilOperation.LoadLocal or CilOperation.StoreLocal))
        {
            return;
        }
        var local = CilOperandReader.GetIndex(instruction);
        if (!candidates.Contains(local))
        {
            return;
        }
        if (instruction.Operation == CilOperation.LoadLocal)
        {
            live.Add(local);
        }
        else
        {
            live.Remove(local);
        }
    }

    private static void ApplyDefinition(
        CilInstruction instruction, HashSet<int> candidates, HashSet<int> origins, bool inLoop)
    {
        if (instruction.Operation != CilOperation.StoreLocal)
        {
            return;
        }
        var local = CilOperandReader.GetIndex(instruction);
        if (inLoop && candidates.Contains(local))
        {
            origins.Add(local);
        }
        else
        {
            origins.Remove(local);
        }
    }
}
