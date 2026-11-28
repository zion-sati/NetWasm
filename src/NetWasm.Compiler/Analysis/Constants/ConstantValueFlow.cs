// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// A constant a local holds: an int32 value, or a null reference.
internal readonly record struct KnownConstant(bool IsNull, int Value)
{
    public static KnownConstant Null => new(true, 0);

    public static KnownConstant Int32(int value) => new(false, value);
}

// The constant each local load reads, whichever way control reaches it: a read-only view, by the load's offset.
internal sealed class ConstantValueFacts(IReadOnlyDictionary<int, KnownConstant> loads) : IEntryLookup<int, KnownConstant>
{
    public static ConstantValueFacts None { get; } = new(new Dictionary<int, KnownConstant>());

    // The constant the ldloc at this offset reads on every path to it.
    public bool TryGetValue(int offset, out KnownConstant value) => loads.TryGetValue(offset, out value);
}

// Which constants locals hold across a method's blocks: a value pushed in one block and stored to a local in another,
// or a local stored before a call and read after it, as an inlined callee's guard reads its argument after the bounds
// checks of the arguments that follow it.
internal interface IConstantValueFlow
{
    ConstantValueFacts Solve(ControlFlowGraph graph);
}

// Forward over the graph. A block starts with what every normal predecessor agrees on; an exception handler, a filter,
// and a catch handler a filter leads to, start knowing nothing (the graph has no edge from endfilter to its handler), and
// so does any reachable block the graph gives no processed predecessor, so a block the analysis never reached cannot
// leave a join to the paths it did. Only int32 and object-reference locals whose address is never taken are followed, so no store
// through an address goes unseen, and none that a finally or fault handler stores: the graph takes a leave straight
// to its target, past the finally that runs on the way. An instruction whose stack effect is unknown forgets the
// evaluation stack, not the locals: nothing but a store or an address writes a local. The state from block to block
// holds only the followed locals some block reads before it writes them; every other followed local is written before
// it is read in each block that reads it, as each condition's local in Debug IL is, so its value lives within a block.
// Holding every local at every block would take memory quadratic in a long else-if chain's length in Debug IL. Those
// locals are a LocalValueTrie, which blocks share where they do not store: a loop's counter is a local of its own, so
// an array of them all at every block grew with a method's loops squared.
internal sealed class ConstantValueFlow(IStackEffectResolver effects) : IConstantValueFlow
{
    private readonly IStackEffectResolver _effects = effects ?? throw new ArgumentNullException(nameof(effects));

    public ConstantValueFacts Solve(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var body = graph.MethodBody;
        var tracked = TrackedLocals(body);
        if (tracked is null) return ConstantValueFacts.None;

        var blocks = graph.Blocks;
        var locals = new BlockLocalValues<KnownConstant?>(blocks, tracked.Length, local => tracked[local]);
        var handlerStarts = HandlerStarts(body);
        bool IsUnknownEntry(int index) => IsHandlerEntry(graph, index) || handlerStarts.Contains(blocks[index].StartOffset);
        var entries = new State?[blocks.Length];
        entries[graph.Entry.Index] = State.Start(locals.Crossing);
        var pending = new Queue<int>();
        var queued = new bool[blocks.Length];
        void Enqueue(int index)
        {
            if (queued[index]) return;
            pending.Enqueue(index);
            queued[index] = true;
        }
        Enqueue(graph.Entry.Index);
        for (var index = 0; index < blocks.Length; index++)
        {
            if (IsUnknownEntry(index) && index != graph.Entry.Index)
            {
                entries[index] = State.Unknown(locals.Crossing);
                Enqueue(index);
            }
        }

        // A reachable block is the entry, a handler or filter start, or a successor of a block reached here; a block
        // left without a state yields no facts.
        while (pending.TryDequeue(out var block))
        {
            queued[block] = false;
            var exit = Transfer(blocks[block], entries[block]!.Copy(), body, tracked, locals, loads: null);
            foreach (var successor in graph.Successors[block])
            {
                if (IsUnknownEntry(successor)) continue;
                var merged = entries[successor] is { } known ? State.Meet(known, exit) : exit.Copy();
                if (entries[successor] is { } previous && previous.SameAs(merged)) continue;
                entries[successor] = merged;
                Enqueue(successor);
            }
        }

        var facts = new Dictionary<int, KnownConstant>();
        foreach (var block in blocks)
        {
            if (entries[block.Index] is { } entry) Transfer(block, entry.Copy(), body, tracked, locals, facts);
        }
        return facts.Count == 0 ? ConstantValueFacts.None : new ConstantValueFacts(facts);
    }

    // The int32 and object-reference locals no instruction takes the address of and no finally or fault handler
    // stores, or null when there are none.
    private static bool[]? TrackedLocals(CilMethodBody body)
    {
        var tracked = new bool[body.Locals.Length];
        var any = false;
        for (var index = 0; index < tracked.Length; index++)
        {
            tracked[index] = body.Locals[index] is CliValueKind.I4 or CliValueKind.ManagedReference;
            any |= tracked[index];
        }
        if (!any) return null;
        var stored = false;
        (int Start, int End)[]? cleanup = null;
        foreach (var instruction in body.Instructions)
        {
            if (instruction is { Operand: CilOperand.Index local } && (uint)local.Value < (uint)tracked.Length)
            {
                if (instruction.Operation == CilOperation.LoadLocalAddress ||
                    (instruction.Operation == CilOperation.StoreLocal &&
                        InAny(cleanup ??= FinallyAndFaultHandlers(body), instruction.Offset)))
                {
                    tracked[local.Value] = false;
                }
                stored |= instruction.Operation == CilOperation.StoreLocal;
            }
        }
        return stored && Array.IndexOf(tracked, true) >= 0 ? tracked : null;
    }

    // The offsets finally and fault handlers cover, as sorted ranges that neither overlap nor touch, so whether a
    // handler holds a store is a binary search: a scan of every region for each store made a method with N protected
    // blocks cost N squared.
    private static (int Start, int End)[] FinallyAndFaultHandlers(CilMethodBody body)
    {
        var ranges = body.ExceptionRegions
            .Where(static region => region.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault &&
                region.HandlerLength > 0)
            .Select(static region => (Start: region.HandlerOffset, End: region.HandlerOffset + region.HandlerLength))
            .OrderBy(static range => range.Start)
            .ToList();
        var merged = new List<(int Start, int End)>(ranges.Count);
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else
                merged.Add(range);
        }
        return [.. merged];
    }

    private static bool InAny((int Start, int End)[] ranges, int offset)
    {
        var low = 0;
        var high = ranges.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (offset < ranges[middle].Start) high = middle - 1;
            else if (offset >= ranges[middle].End) low = middle + 1;
            else return true;
        }
        return false;
    }

    // The first instruction of every handler and filter.
    private static HashSet<int> HandlerStarts(CilMethodBody body)
    {
        var starts = new HashSet<int>();
        foreach (var region in body.ExceptionRegions)
        {
            starts.Add(region.HandlerOffset);
            if (region.FilterOffset is { } filter) starts.Add(filter);
        }
        return starts;
    }

    // The graph's exceptional predecessors hold every block, most with none.
    private static bool IsHandlerEntry(ControlFlowGraph graph, int block) =>
        !graph.ExceptionalPredecessors[block].IsDefaultOrEmpty;

    private State Transfer(BasicBlock block, State state, CilMethodBody body, bool[] tracked,
        BlockLocalValues<KnownConstant?> locals, Dictionary<int, KnownConstant>? loads)
    {
        foreach (var instruction in block.Instructions)
        {
            switch (instruction.Operation)
            {
                case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 constant:
                    state.Push(KnownConstant.Int32(constant.Value));
                    break;
                case CilOperation.LoadNull:
                    state.Push(KnownConstant.Null);
                    break;
                case CilOperation.LoadLocal when instruction.Operand is CilOperand.Index local &&
                    (uint)local.Value < (uint)tracked.Length:
                    var value = tracked[local.Value] ? locals.Read(state.Locals, local.Value) : null;
                    if (value is { } known && loads is not null) loads[instruction.Offset] = known;
                    state.Push(value);
                    break;
                case CilOperation.StoreLocal when instruction.Operand is CilOperand.Index local &&
                    (uint)local.Value < (uint)tracked.Length:
                    var stored = state.Pop();
                    if (tracked[local.Value]) state.Locals = locals.Write(state.Locals, local.Value, Accepts(body.Locals[local.Value], stored));
                    break;
                case CilOperation.Duplicate:
                    var top = state.Pop();
                    state.Push(top);
                    state.Push(top);
                    break;
                case CilOperation.Branch:
                    break;
                case CilOperation.Leave:
                    state.Empty();
                    break;
                case CilOperation.Throw:
                    state.Pop();
                    break;
                case CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                    CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
                    CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                    CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
                    CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned:
                    state.Pop();
                    state.Pop();
                    break;
                default:
                    if (_effects.Resolve(instruction) is var (pops, pushes))
                    {
                        for (var pop = 0; pop < pops; pop++) state.Pop();
                        for (var push = 0; push < pushes; push++) state.Push(null);
                    }
                    else
                    {
                        state.Forget();
                    }
                    break;
            }
        }
        locals.EndBlock();
        return state;
    }

    // What a store leaves in a local of this kind: a null in a reference local, an int32 in an int32 local.
    private static KnownConstant? Accepts(CliValueKind kind, KnownConstant? value) => value switch
    {
        { IsNull: true } when kind == CliValueKind.ManagedReference => value,
        { IsNull: false } when kind == CliValueKind.I4 => value,
        _ => null,
    };

    private sealed class State
    {
        private static readonly Func<KnownConstant?, KnownConstant?, KnownConstant?> MeetValue =
            static (left, right) => left == right ? left : null;

        private List<KnownConstant?>? _stack;

        private State(LocalValueTrie<KnownConstant?> locals, List<KnownConstant?>? stack)
        {
            Locals = locals;
            _stack = stack;
        }

        // Persistent: a copy shares it, and a store replaces it.
        internal LocalValueTrie<KnownConstant?> Locals { get; set; }

        internal static State Start(int locals) => new(LocalValueTrie<KnownConstant?>.Empty(locals), []);

        // A handler's start: nothing known of the locals or of the stack.
        internal static State Unknown(int locals) => new(LocalValueTrie<KnownConstant?>.Empty(locals), null);

        internal State Copy() => new(Locals, _stack is null ? null : [.. _stack]);

        internal void Push(KnownConstant? value) => _stack?.Add(value);

        internal KnownConstant? Pop()
        {
            if (_stack is null || _stack.Count == 0)
            {
                _stack = null;
                return null;
            }
            var value = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return value;
        }

        internal void Empty() => _stack = [];

        internal void Forget() => _stack = null;

        internal static State Meet(State left, State right)
        {
            var locals = left.Locals.Meet(right.Locals, MeetValue);
            List<KnownConstant?>? stack = null;
            if (left._stack is { } leftStack && right._stack is { } rightStack && leftStack.Count == rightStack.Count)
            {
                stack = new List<KnownConstant?>(leftStack.Count);
                for (var index = 0; index < leftStack.Count; index++)
                {
                    stack.Add(leftStack[index] == rightStack[index] ? leftStack[index] : null);
                }
            }
            return new State(locals, stack);
        }

        internal bool SameAs(State other)
        {
            if (!Locals.SameAs(other.Locals)) return false;
            if (_stack is null || other._stack is null) return _stack is null && other._stack is null;
            // One block's states: validated IL enters it at one depth.
            for (var index = 0; index < _stack.Count; index++)
            {
                if (_stack[index] != other._stack[index]) return false;
            }
            return true;
        }
    }
}
