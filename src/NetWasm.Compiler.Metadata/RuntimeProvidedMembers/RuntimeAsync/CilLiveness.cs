// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// Which locals and arguments the code after an instruction may read before it writes them: what a suspension at that
// instruction must keep. Conservative where control is not explicit: every instruction in a protected block may
// continue at its handler or filter, the end of a finally or fault handler at any leave target, and a local or
// argument with an address that may be read through anywhere stays live everywhere. An address used and dropped by the
// instruction consuming it reads the value there, or writes it in full (CilAddressUses).
// Per variable, a walk backward from the instructions that read it, stopping where it is written: the cost is each
// variable's live range, not the method's instructions times its variables. An end of a finally or fault handler
// reaches every leave target through one shared node rather than an edge to each, and an instruction's exceptional
// edges come from the protected blocks that hold it, found in one sweep.
internal sealed class CilLiveness
{
    private readonly Dictionary<int, int> _positions;
    private readonly BitArray _exposedLocals;
    private readonly BitArray _exposedArguments;
    private readonly int _locals;
    // The variables live after each recorded position: locals by index, arguments after them.
    private readonly Dictionary<int, HashSet<int>> _liveAfter;

    private CilLiveness(Dictionary<int, int> positions, BitArray exposedLocals, BitArray exposedArguments, int locals,
        Dictionary<int, HashSet<int>> liveAfter)
    {
        _positions = positions;
        _exposedLocals = exposedLocals;
        _exposedArguments = exposedArguments;
        _locals = locals;
        _liveAfter = liveAfter;
    }

    // Live after the instruction at the offset.
    internal bool LocalLiveAfter(int offset, int local) =>
        _exposedLocals[local] || LiveSet(offset).Contains(local);

    internal bool ArgumentLiveAfter(int offset, int argument) =>
        _exposedArguments[argument] || LiveSet(offset).Contains(_locals + argument);

    // The locals and the arguments live after the instruction at the offset, each in ascending order.
    internal (int[] Locals, int[] Arguments) LiveAfter(int offset)
    {
        var live = LiveSet(offset);
        var locals = new List<int>();
        var arguments = new List<int>();
        for (var local = 0; local < _exposedLocals.Length; local++)
        {
            if (_exposedLocals[local]) locals.Add(local);
        }
        for (var argument = 0; argument < _exposedArguments.Length; argument++)
        {
            if (_exposedArguments[argument]) arguments.Add(argument);
        }
        foreach (var variable in live)
        {
            if (variable < _locals) locals.Add(variable);
            else arguments.Add(variable - _locals);
        }
        locals.Sort();
        arguments.Sort();
        return ([.. locals.Distinct()], [.. arguments.Distinct()]);
    }

    private HashSet<int> LiveSet(int offset) =>
        _liveAfter.TryGetValue(_positions[offset], out var live)
            ? live
            : throw new InvalidOperationException($"Liveness after IL_{offset:x4} was not recorded.");

    // Records every instruction's live-out set: for small bodies and tests.
    public static CilLiveness Compute(CilMethodBody body, int arguments, CilAddressUses addresses) =>
        Compute(body, arguments, addresses, null);

    // Records the live-out sets of the instructions at the offsets given (the awaits), or of every instruction.
    public static CilLiveness Compute(CilMethodBody body, int arguments, CilAddressUses addresses,
        IReadOnlyCollection<int>? recordedOffsets)
    {
        var instructions = body.Instructions;
        var count = instructions.Length;
        var locals = body.Locals.Length;
        var positions = new Dictionary<int, int>(count);
        for (var position = 0; position < count; position++) positions[instructions[position].Offset] = position;
        // One extra node: where the end of a finally or fault handler goes, on to every leave target.
        var leaveSink = count;
        var successors = new List<int>[count + 1];
        for (var position = 0; position <= count; position++) successors[position] = [];
        foreach (var instruction in instructions)
        {
            if (instruction is { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget leave } &&
                positions.TryGetValue(leave.Offset, out var target))
            {
                successors[leaveSink].Add(target);
            }
        }
        for (var position = 0; position < count; position++)
        {
            var instruction = instructions[position];
            var next = successors[position];
            if (FallsThrough(instruction.Operation) && position + 1 < count) next.Add(position + 1);
            if (instruction.Operand is CilOperand.BranchTarget branch && positions.TryGetValue(branch.Offset, out var target))
                next.Add(target);
            if (instruction.Operand is CilOperand.SwitchTargets table)
            {
                foreach (var offset in table.Offsets)
                {
                    if (positions.TryGetValue(offset, out var arm)) next.Add(arm);
                }
            }
            if (instruction.Operation == CilOperation.EndFinally) next.Add(leaveSink);
        }
        AddFilterEnds(body, positions, successors);
        AddExceptionalEdges(body, positions, successors);

        var predecessors = new List<int>[count + 1];
        for (var position = 0; position <= count; position++) predecessors[position] = [];
        for (var position = 0; position <= count; position++)
        {
            foreach (var successor in successors[position]) predecessors[successor].Add(position);
        }

        // What each instruction does to the variables it touches, in order: its own load or store, then a write and a
        // read through an address it consumes.
        var touches = new List<(int Variable, Effect Effect)>?[count];
        var reads = new Dictionary<int, List<int>>();
        void Touch(int position, int variable, Effect effect) =>
            (touches[position] ??= []).Add((variable, effect));
        for (var position = 0; position < count; position++)
        {
            switch (instructions[position])
            {
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local }:
                    Touch(position, local.Value, Effect.Write);
                    break;
                case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local }:
                    Touch(position, local.Value, Effect.Read);
                    break;
                case { Operation: CilOperation.StoreArgument, Operand: CilOperand.Index argument }:
                    Touch(position, locals + argument.Value, Effect.Write);
                    break;
                case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index argument }:
                    Touch(position, locals + argument.Value, Effect.Read);
                    break;
            }
            if (addresses.Writes(position, out var written))
                Touch(position, written.Local ? written.Index : locals + written.Index, Effect.Write);
            if (addresses.Reads(position, out var read))
                Touch(position, read.Local ? read.Index : locals + read.Index, Effect.Read);
            if (touches[position] is { } list)
            {
                foreach (var (variable, _) in list)
                {
                    if (LiveIn(list, variable, liveOut: false))
                    {
                        if (!reads.TryGetValue(variable, out var at)) reads[variable] = at = [];
                        if (at.Count == 0 || at[^1] != position) at.Add(position);
                    }
                }
            }
        }

        bool Exposed(int variable) => variable < locals
            ? addresses.ExposedLocals[variable]
            : addresses.ExposedArguments[variable - locals];

        var recordAll = recordedOffsets is null;
        var recorded = new HashSet<int>();
        if (recordedOffsets is not null)
        {
            foreach (var offset in recordedOffsets)
            {
                if (positions.TryGetValue(offset, out var position)) recorded.Add(position);
            }
        }
        var liveAfter = new Dictionary<int, HashSet<int>>();
        if (recordAll)
        {
            for (var position = 0; position < count; position++) liveAfter[position] = [];
        }
        else
        {
            foreach (var position in recorded) liveAfter[position] = [];
        }

        // Stamps mark, per variable, the positions found live-in and live-out without clearing between variables.
        var liveInStamp = new int[count + 1];
        var liveOutStamp = new int[count + 1];
        var pending = new Stack<int>();
        var stamp = 0;
        foreach (var (variable, starts) in reads)
        {
            if (Exposed(variable)) continue;
            stamp++;
            foreach (var position in starts)
            {
                if (liveInStamp[position] == stamp) continue;
                liveInStamp[position] = stamp;
                pending.Push(position);
            }
            while (pending.Count > 0)
            {
                var position = pending.Pop();
                foreach (var predecessor in predecessors[position])
                {
                    if (liveOutStamp[predecessor] == stamp) continue;
                    liveOutStamp[predecessor] = stamp;
                    if (predecessor < count && liveAfter.TryGetValue(predecessor, out var live)) live.Add(variable);
                    if (liveInStamp[predecessor] == stamp) continue;
                    if (predecessor < count && touches[predecessor] is { } list && !LiveIn(list, variable, liveOut: true))
                        continue;
                    liveInStamp[predecessor] = stamp;
                    pending.Push(predecessor);
                }
            }
        }
        return new(positions, addresses.ExposedLocals, addresses.ExposedArguments, locals, liveAfter);
    }

    // Whether the variable is live before the instruction given whether it is live after: its effects applied in
    // order, the last that touches the variable deciding.
    private static bool LiveIn(List<(int Variable, Effect Effect)> touches, int variable, bool liveOut)
    {
        var live = liveOut;
        foreach (var (touched, effect) in touches)
        {
            if (touched == variable) live = effect == Effect.Read;
        }
        return live;
    }

    // A filter runs from its offset up to its handler, which follows it: an end of the filter goes on to the handler.
    private static void AddFilterEnds(CilMethodBody body, Dictionary<int, int> positions, List<int>[] successors)
    {
        foreach (var region in body.ExceptionRegions)
        {
            if (region.FilterOffset is not { } filter ||
                !positions.TryGetValue(filter, out var first) ||
                !positions.TryGetValue(region.HandlerOffset, out var handler))
            {
                continue;
            }
            for (var position = first; position < handler; position++)
            {
                if (body.Instructions[position].Operation == CilOperation.EndFilter) successors[position].Add(handler);
            }
        }
    }

    // Every instruction in a protected block may go on to its handler or filter: the blocks holding each instruction,
    // found in one sweep over the instructions with the blocks open there (nested, so a stack).
    private static void AddExceptionalEdges(CilMethodBody body, Dictionary<int, int> positions, List<int>[] successors)
    {
        var instructions = body.Instructions;
        var blocks = body.ExceptionRegions
            .GroupBy(region => (Start: region.TryOffset, End: region.TryOffset + region.TryLength))
            .Select(group => (group.Key.Start, group.Key.End, Entries: group
                .SelectMany(region => region.FilterOffset is { } filter
                    ? new[] { region.HandlerOffset, filter }
                    : new[] { region.HandlerOffset })
                .Where(positions.ContainsKey)
                .Select(offset => positions[offset])
                .ToArray()))
            .OrderBy(block => block.Start)
            .ThenByDescending(block => block.End)
            .ToArray();
        var open = new List<int>();
        var next = 0;
        for (var position = 0; position < instructions.Length; position++)
        {
            var offset = instructions[position].Offset;
            open.RemoveAll(index => blocks[index].End <= offset);
            while (next < blocks.Length && blocks[next].Start <= offset)
            {
                if (blocks[next].End > offset) open.Add(next);
                next++;
            }
            foreach (var index in open) successors[position].AddRange(blocks[index].Entries);
        }
    }

    internal static bool FallsThrough(CilOperation operation) => operation is not (CilOperation.Branch or CilOperation.Leave or
        CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or CilOperation.EndFinally or CilOperation.EndFilter);

    private enum Effect
    {
        Read,
        Write,
    }
}
