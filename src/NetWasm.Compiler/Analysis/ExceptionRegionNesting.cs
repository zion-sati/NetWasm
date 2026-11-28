// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// How a method body's protected blocks and handlers nest, indexed once (one sweep over the instructions and prefix
// counts) so that each question takes time in proportion to its answer: the protected blocks around an instruction,
// innermost first, and each block's enclosing one; whether a handler or a filter covers an instruction; and how many
// protected blocks or handlers start in a handler. Protected blocks and handlers nest properly, as valid IL requires.
internal sealed class ExceptionRegionNesting
{
    private readonly ImmutableArray<CilExceptionRegion> _regions;
    private readonly Dictionary<int, int> _positions;
    private readonly int[] _innermostTry;
    private readonly int[] _enclosingTry;
    private readonly int[] _handlerCover;
    private readonly int[] _tryStartsBefore;
    private readonly int[] _handlerStartsBefore;

    public ExceptionRegionNesting(ImmutableArray<CilInstruction> instructions, ImmutableArray<CilExceptionRegion> regions)
    {
        _regions = regions;
        var count = instructions.Length;
        _positions = new Dictionary<int, int>(count + 1);
        for (var position = 0; position < count; position++) _positions.Add(instructions[position].Offset, position);
        if (count != 0) _positions.TryAdd(instructions[^1].NextOffset, count);

        // The protected blocks open at each instruction, outermost at the bottom: a block opens at its first instruction
        // (outer before inner when two start together) and closes at its end.
        _innermostTry = new int[count];
        _enclosingTry = new int[regions.Length];
        Array.Fill(_enclosingTry, -1);
        var byStart = Enumerable.Range(0, regions.Length)
            .OrderBy(region => regions[region].TryOffset)
            .ThenByDescending(region => regions[region].TryLength)
            .ToArray();
        var open = new Stack<int>();
        var next = 0;
        for (var position = 0; position < count; position++)
        {
            var offset = instructions[position].Offset;
            while (open.TryPeek(out var top) && regions[top].TryOffset + regions[top].TryLength <= offset) open.Pop();
            while (next < byStart.Length && regions[byStart[next]].TryOffset <= offset)
            {
                var region = byStart[next++];
                _enclosingTry[region] = open.TryPeek(out var outer) ? outer : -1;
                open.Push(region);
            }
            _innermostTry[position] = open.TryPeek(out var innermost) ? innermost : -1;
        }

        // Handler and filter cover, and protected block and handler starts, counted over positions.
        var coverChanges = new int[count + 1];
        var tryStarts = new int[count + 1];
        var handlerStarts = new int[count + 1];
        foreach (var region in regions)
        {
            coverChanges[Position(region.FilterOffset ?? region.HandlerOffset)]++;
            coverChanges[Position(region.HandlerOffset + region.HandlerLength)]--;
            tryStarts[Position(region.TryOffset)]++;
            handlerStarts[Position(region.FilterOffset ?? region.HandlerOffset)]++;
        }
        _handlerCover = new int[count + 1];
        _tryStartsBefore = new int[count + 2];
        _handlerStartsBefore = new int[count + 2];
        for (int position = 0, cover = 0; position <= count; position++)
        {
            cover += coverChanges[position];
            _handlerCover[position] = cover;
            _tryStartsBefore[position + 1] = _tryStartsBefore[position] + tryStarts[position];
            _handlerStartsBefore[position + 1] = _handlerStartsBefore[position] + handlerStarts[position];
        }
    }

    // The position of the instruction at an offset (the instruction count for the body's end).
    internal int Position(int offset) => _positions[offset];

    // The protected block that most closely encloses this one's, or -1.
    internal int EnclosingTry(int region) => _enclosingTry[region];

    // The protected blocks that a transfer from one offset to another leaves: those around from but not around target,
    // innermost first.
    internal IEnumerable<int> TriesLeft(int from, int target)
    {
        var position = Position(from);
        for (var region = position < _innermostTry.Length ? _innermostTry[position] : -1;
             region >= 0 && !TryHolds(region, target);
             region = _enclosingTry[region])
        {
            yield return region;
        }
    }

    // Whether a handler or a filter covers the instruction at an offset.
    internal bool InHandler(int offset) => _handlerCover[Position(offset)] != 0;

    // Whether a region's handler holds a protected block, or another handler or filter.
    internal bool HandlerHoldsABlock(int region)
    {
        var handler = _regions[region];
        var first = Position(handler.HandlerOffset);
        var end = Position(handler.HandlerOffset + handler.HandlerLength);
        return _tryStartsBefore[end] - _tryStartsBefore[first] != 0 ||
            _handlerStartsBefore[end] - _handlerStartsBefore[first + 1] != 0;
    }

    private bool TryHolds(int region, int offset) =>
        offset >= _regions[region].TryOffset && offset < _regions[region].TryOffset + _regions[region].TryLength;
}
