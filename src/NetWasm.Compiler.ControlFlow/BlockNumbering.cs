// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

// Array positions for a set of blocks: each block's own index while the set holds at least half the indices below its
// largest, otherwise its place in a map. An analysis of a few blocks of a large graph (a handler's, a protected
// block's) then allocates for the few: arrays the size of the graph for each of a method's N protected blocks made it
// cost N squared.
internal sealed class BlockNumbering
{
    private readonly Dictionary<int, int>? _numbers;
    private readonly int[]? _blocks;

    private BlockNumbering(int count, Dictionary<int, int>? numbers, int[]? blocks)
    {
        Count = count;
        _numbers = numbers;
        _blocks = blocks;
    }

    // The positions are 0 to Count - 1.
    internal int Count { get; }

    internal static BlockNumbering For(ImmutableHashSet<int> blocks)
    {
        var span = 0;
        foreach (var block in blocks) span = Math.Max(span, block + 1);
        if (span <= 2 * blocks.Count) return new(span, null, null);
        var numbers = new Dictionary<int, int>(blocks.Count);
        var order = new int[blocks.Count];
        foreach (var block in blocks)
        {
            order[numbers.Count] = block;
            numbers.Add(block, numbers.Count);
        }
        return new(order.Length, numbers, order);
    }

    // A block of the set's position.
    internal int Number(int block) => _numbers is null ? block : _numbers[block];

    // The block at a position.
    internal int Block(int number) => _blocks is null ? number : _blocks[number];
}
