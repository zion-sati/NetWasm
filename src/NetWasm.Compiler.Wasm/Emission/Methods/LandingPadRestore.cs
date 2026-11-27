// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// Where a landing pad returns one frame arena to: where the method stood when the protected block began. Nothing is
// kept as the block begins unless the source is BlockMark.
internal enum ArenaRestoreSource
{
    // The program records nothing in the arena (stack-trace frames when it records no stack traces).
    None,

    // The method's own frame, entered in its prologue on every path, is the newest it holds at the block: the arena
    // returns to that frame.
    OwnedFrame,

    // The method holds no frame in the arena and the block lies in a loop: the arena returns to where it stood after
    // the prologue, read then, once for every pass through the loop.
    EntryMark,

    // The method may enter frames of its own in the arena between its prologue and the block (a lazy root frame, a
    // localloc block, a frame placed on terminal branches, an exception cell in a handler the block is in), or holds none
    // and the block lies in no loop, so it begins at most once a call and a path that skips it reads nothing: the arena
    // returns to where it stood as the block began, read then.
    BlockMark,
}

// A protected block's landing pad restore: one source per arena.
internal sealed record LandingPadRestore(ArenaRestoreSource Root, ArenaRestoreSource Value, ArenaRestoreSource StackTrace)
{
    // Whether the block reads a mark as it begins.
    public bool ReadsAtBlockEntry => Root == ArenaRestoreSource.BlockMark || Value == ArenaRestoreSource.BlockMark;
}

internal interface ILandingPadRestorePlanner
{
    // The restore of each protected block that keeps no registered frame's marks, by group.
    ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore> Plan(StructuredMethod method, MethodEmissionContext context);
}

// A method's root frame is its own at every block when it is entered in the prologue (not lazy, not placed on terminal
// branches) and no exception cell sits above it there (the block is in no handler); its value frame when it is entered in
// the prologue and the method allocates no localloc block, which enters frames of its own; a method with a filter
// environment enters frames of its own for it, so it reads every mark at the block. An arena the method holds no frame
// in is read once after the prologue, which a block in a loop needs (it reads nothing per pass) and which serves every
// block at once; when only one block reads it and that block lies in no loop (no backward branch spans its start), the
// block reads it as it begins instead, so a path that skips the block reads nothing. Stack-trace frames: a method that
// records one does so in its prologue, and one that records none has none, so the read after the prologue serves both.
internal sealed class LandingPadRestorePlanner : ILandingPadRestorePlanner
{
    public ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore> Plan(StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        if (method.ExceptionGroups.Count == 0) return ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore>.Empty;
        var placed = context.TerminalBranchValueFramePlacement is not null || context.FilterEnvironment.Size != 0;
        var instructions = method.Header.Instructions;
        var localloc = instructions.Any(instruction => instruction.Operation == CilOperation.LocalAllocate);
        var rootFrame = placed || context.LazyRootFrame is not null
            ? ArenaRestoreSource.BlockMark
            : context.RootSlotCount == 0 ? ArenaRestoreSource.EntryMark : ArenaRestoreSource.OwnedFrame;
        var value = placed || localloc
            ? ArenaRestoreSource.BlockMark
            : context.ValueLayout.Size == 0 ? ArenaRestoreSource.EntryMark : ArenaRestoreSource.OwnedFrame;
        var stackTrace = context.RuntimeImportSelection.IncludeStackTrace ? ArenaRestoreSource.EntryMark : ArenaRestoreSource.None;

        var offsets = instructions.Select(instruction => instruction.Offset).ToArray();
        // A block lies in a handler when more handlers cover its start than its own.
        static (int, int) Range(StructuredExceptionClause clause) =>
            (clause.FilterOffset ?? clause.HandlerOffset, clause.HandlerOffset + clause.HandlerLength);
        var handlerCover = Covers(offsets,
            method.ExceptionGroups.Values.SelectMany(group => group.Clauses.Select(Range)));
        bool InHandler(StructuredExceptionGroup group) => handlerCover(group.TryOffset) >
            group.Clauses.Count(clause => Range(clause) is var (start, end) && group.TryOffset >= start && group.TryOffset < end);
        var loopCover = Covers(offsets, BackwardSpans(instructions));
        bool Looped(int offset) => loopCover(offset) != 0;
        var groups = method.ExceptionGroups.Values.ToArray();
        var rootReaders = rootFrame == ArenaRestoreSource.EntryMark
            ? groups.Where(group => !InHandler(group)).ToArray()
            : [];
        var valueReaders = value == ArenaRestoreSource.EntryMark ? groups : [];
        StructuredExceptionGroupId? rootAtBlock = rootReaders is [var onlyRoot] && !Looped(onlyRoot.TryOffset) ? onlyRoot.Id : null;
        StructuredExceptionGroupId? valueAtBlock = valueReaders is [var onlyValue] && !Looped(onlyValue.TryOffset) ? onlyValue.Id : null;

        var plans = ImmutableDictionary.CreateBuilder<StructuredExceptionGroupId, LandingPadRestore>();
        foreach (var group in groups)
        {
            plans.Add(group.Id, new(
                InHandler(group) || group.Id.Equals(rootAtBlock) ? ArenaRestoreSource.BlockMark : rootFrame,
                group.Id.Equals(valueAtBlock) ? ArenaRestoreSource.BlockMark : value,
                stackTrace));
        }
        return plans.ToImmutable();
    }

    // The spans from a backward branch's target to the branch: every cycle through an instruction crosses it from above
    // with such a branch, so an offset no span covers runs at most once a call.
    private static IEnumerable<(int Start, int End)> BackwardSpans(ImmutableArray<CilInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            var targets = instruction.Operand switch
            {
                CilOperand.BranchTarget target => ImmutableArray.Create(target.Offset),
                CilOperand.SwitchTargets table => table.Offsets,
                _ => ImmutableArray<int>.Empty,
            };
            foreach (var target in targets)
            {
                if (target <= instruction.Offset) yield return (target, instruction.NextOffset);
            }
        }
    }

    // How many of the offset ranges cover an instruction's offset, counted over instruction positions once (an offset maps
    // to the first instruction at or after it).
    private static Func<int, int> Covers(int[] offsets, IEnumerable<(int Start, int End)> ranges)
    {
        var count = offsets.Length;
        int Position(int offset)
        {
            var found = Array.BinarySearch(offsets, offset);
            return found >= 0 ? found : ~found;
        }
        var changes = new int[count + 1];
        var any = false;
        foreach (var (start, end) in ranges)
        {
            changes[Position(start)]++;
            changes[Position(end)]--;
            any = true;
        }
        if (!any) return static _ => 0;
        var covers = new int[count];
        for (int position = 0, depth = 0; position < count; position++)
        {
            depth += changes[position];
            covers[position] = depth;
        }
        return offset => Array.BinarySearch(offsets, offset) is var position and >= 0 ? covers[position] : 0;
    }
}
