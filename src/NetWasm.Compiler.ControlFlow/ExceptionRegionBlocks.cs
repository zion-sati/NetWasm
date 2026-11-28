// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

// A method's exception regions against its blocks, indexed once for the passes that ask per loop or per continuation:
// the regions whose protected block starts at a block, the blocks an offset range holds, whether the protected blocks
// and handlers around a block hold an offset too (through their ProtectedRangeNesting), and every region's start.
// Scanning every region for each loop took the loops times the regions; each answer here costs what it returns.
internal sealed class ExceptionRegionBlocks
{
    private readonly ImmutableArray<CilExceptionRegion> _regions;
    private readonly int[] _startOffsets;
    private readonly int[] _blocksInOffsetOrder;
    private readonly Dictionary<int, int> _positions = [];
    private readonly Dictionary<int, List<CilExceptionRegion>> _tryRegionsByStart = [];
    private readonly (int Start, int End)[] _ranges;
    private readonly Lazy<ProtectedRangeNesting?> _nesting;

    internal ExceptionRegionBlocks(CilMethodBody body, ImmutableArray<BasicBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(body);
        _regions = body.ExceptionRegions;
        _blocksInOffsetOrder = new int[blocks.Length];
        _startOffsets = new int[blocks.Length];
        for (var index = 0; index < blocks.Length; index++)
        {
            (_blocksInOffsetOrder[index], _startOffsets[index]) = (blocks[index].Index, blocks[index].StartOffset);
        }
        Array.Sort(_startOffsets, _blocksInOffsetOrder);
        for (var position = 0; position < _blocksInOffsetOrder.Length; position++)
        {
            _positions[_blocksInOffsetOrder[position]] = position;
        }
        var starts = ImmutableHashSet.CreateBuilder<int>();
        foreach (var region in body.ExceptionRegions)
        {
            if (BlockAt(region.TryOffset) is int tryBlock)
            {
                if (!_tryRegionsByStart.TryGetValue(tryBlock, out var regions))
                {
                    _tryRegionsByStart.Add(tryBlock, regions = []);
                }
                regions.Add(region);
            }
            starts.Add(region.TryOffset);
            starts.Add(region.HandlerOffset);
            if (region.FilterOffset is int filter)
            {
                starts.Add(filter);
            }
        }
        RegionStartOffsets = starts.ToImmutable();
        _ranges = [.. _regions.SelectMany(region => new[]
        {
            (region.TryOffset, region.TryOffset + region.TryLength),
            (region.HandlerOffset, region.HandlerOffset + region.HandlerLength),
        })];
        _nesting = new(() => ProtectedRangeNesting.Build(_ranges, _startOffsets));
    }

    // The offsets where a protected block, a handler or a filter starts.
    internal ImmutableHashSet<int> RegionStartOffsets { get; }

    // The regions whose protected block starts at the block, in the method's order.
    internal IReadOnlyList<CilExceptionRegion> TryRegionsStartingAt(int block) =>
        _tryRegionsByStart.TryGetValue(block, out var regions) ? regions : [];

    // The blocks that start from the offset up to, not including, the offset past the length, in offset order.
    internal IEnumerable<int> BlocksFrom(int offset, int length)
    {
        var index = Array.BinarySearch(_startOffsets, offset);
        for (index = index < 0 ? ~index : index; index < _startOffsets.Length && _startOffsets[index] < offset + length; index++)
        {
            yield return _blocksInOffsetOrder[index];
        }
    }

    // Whether every protected block and handler that holds the block's start holds the offset too. The ranges nest, so
    // the innermost one around the block decides: the ones outside it hold whatever it holds. Ranges that overlap
    // without nesting (IL that validation rejects later) are scanned.
    internal bool RangesAroundAlsoHold(int block, int offset)
    {
        var start = _startOffsets[_positions[block]];
        if (_nesting.Value is not { } nesting)
        {
            return _ranges.All(range => !Holds(range, start) || Holds(range, offset));
        }
        var innermost = nesting.Innermost[_positions[block]];
        return innermost < 0 || Holds(_ranges[innermost], offset);
    }

    private static bool Holds((int Start, int End) range, int offset) => offset >= range.Start && offset < range.End;

    private int? BlockAt(int offset)
    {
        var index = Array.BinarySearch(_startOffsets, offset);
        return index >= 0 ? _blocksInOffsetOrder[index] : null;
    }
}
