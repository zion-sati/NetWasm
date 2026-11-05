using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace NetWasm.Compiler.ControlFlow;

/// <summary>
/// Block sets for the iterative (post-)dominance analyses, as one bit row per block in a
/// single array: an intersection is a word-wise AND instead of an immutable set, so a pass
/// allocates nothing. The results are handed out as immutable sets, each built by adding
/// its block to its immediate (post-)dominator's set where the sets form a tree, so they
/// share structure.
/// </summary>
internal sealed class DominanceBitSets
{
    private readonly ulong[] _bits;
    private readonly int _words;

    public DominanceBitSets(int blockCount)
    {
        _words = (blockCount + 63) >> 6;
        _bits = new ulong[blockCount * _words];
        Scratch = new ulong[_words];
    }

    // A row's worth of words for building a value before it is stored.
    public ulong[] Scratch { get; }

    public static int CountFor(ControlFlowGraph graph, ImmutableHashSet<int> blocks)
    {
        var count = graph.Blocks.Length;
        foreach (var block in blocks) count = Math.Max(count, block + 1);
        return count;
    }

    internal Span<ulong> Row(int block) => _bits.AsSpan(block * _words, _words);

    public static void Set(Span<ulong> row, int block) => row[block >> 6] |= 1UL << (block & 63);

    public static bool Contains(ReadOnlySpan<ulong> row, int block) =>
        (row[block >> 6] & (1UL << (block & 63))) != 0;

    public static void Fill(Span<ulong> row, ImmutableHashSet<int> blocks)
    {
        row.Clear();
        foreach (var block in blocks) Set(row, block);
    }

    public static void And(Span<ulong> row, ReadOnlySpan<ulong> other)
    {
        for (var word = 0; word < row.Length; word++) row[word] &= other[word];
    }

    // Stores the scratch value as the block's row; whether it changed.
    internal bool Update(int block)
    {
        var row = Row(block);
        if (row.SequenceEqual(Scratch)) return false;
        Scratch.CopyTo(row);
        return true;
    }

    internal Dictionary<int, ImmutableHashSet<int>> ToSets(ImmutableHashSet<int> blocks)
    {
        var counts = new Dictionary<int, int>(blocks.Count);
        foreach (var block in blocks)
        {
            var count = 0;
            foreach (var word in Row(block)) count += BitOperations.PopCount(word);
            counts[block] = count;
        }
        var order = new List<int>(blocks);
        order.Sort((left, right) => counts[left] != counts[right]
            ? counts[left].CompareTo(counts[right])
            : left.CompareTo(right));
        var result = new Dictionary<int, ImmutableHashSet<int>>(blocks.Count);
        var expected = new ulong[_words];
        foreach (var block in order)
        {
            var row = Row(block);
            ImmutableHashSet<int>? set = null;
            if (Contains(row, block))
            {
                // A set of the block's (post-)dominator chain is its parent's plus itself.
                for (var word = 0; word < row.Length && set is null; word++)
                {
                    for (var bits = row[word]; bits != 0 && set is null; bits &= bits - 1)
                    {
                        var parent = (word << 6) + BitOperations.TrailingZeroCount(bits);
                        if (parent == block || !result.TryGetValue(parent, out var parentSet) ||
                            counts[parent] != counts[block] - 1)
                        {
                            continue;
                        }
                        Row(parent).CopyTo(expected);
                        Set(expected, block);
                        if (expected.AsSpan().SequenceEqual(row)) set = parentSet.Add(block);
                    }
                }
            }
            result[block] = set ?? Build(row);
        }
        return result;
    }

    private static ImmutableHashSet<int> Build(ReadOnlySpan<ulong> row)
    {
        var builder = ImmutableHashSet.CreateBuilder<int>();
        for (var word = 0; word < row.Length; word++)
        {
            for (var bits = row[word]; bits != 0; bits &= bits - 1)
                builder.Add((word << 6) + BitOperations.TrailingZeroCount(bits));
        }
        return builder.ToImmutable();
    }
}
