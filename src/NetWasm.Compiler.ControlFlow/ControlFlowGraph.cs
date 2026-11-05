using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public sealed record BasicBlock(
    int Index,
    int StartOffset,
    ImmutableArray<CilInstruction> Instructions)
{
    public CilInstruction Terminator => Instructions[^1];
}

/// <summary>
/// Each block's edges, by block index. Blocks are numbered densely from zero, so the edges
/// are an array: a lookup is an index, not a hash, and the graph allocates one array.
/// </summary>
public sealed class BlockEdgeDictionary : IReadOnlyDictionary<int, ImmutableArray<int>>
{
    private readonly ImmutableArray<int>[] _edges;

    private BlockEdgeDictionary(ImmutableArray<int>[] edges) => _edges = edges;

    public ImmutableArray<int> this[int block] =>
        (uint)block < (uint)_edges.Length ? _edges[block] : throw new KeyNotFoundException($"block {block}");

    public int Count => _edges.Length;

    public IEnumerable<int> Keys
    {
        get
        {
            for (var block = 0; block < _edges.Length; block++) yield return block;
        }
    }

    public IEnumerable<ImmutableArray<int>> Values => _edges;

    // Takes ownership of the array; blocks without edges must hold empty arrays.
    internal static BlockEdgeDictionary Over(ImmutableArray<int>[] edges) => new(edges);

    // The edges of a map keyed by block index; blocks it omits have none.
    public static BlockEdgeDictionary From(IReadOnlyDictionary<int, ImmutableArray<int>> edges, int blockCount)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges is BlockEdgeDictionary dense && dense.Count == blockCount)
            return dense;
        var count = blockCount;
        foreach (var block in edges.Keys)
        {
            if (block < 0) throw new ArgumentOutOfRangeException(nameof(edges), "a block index is negative");
            count = Math.Max(count, block + 1);
        }
        var array = new ImmutableArray<int>[count];
        Array.Fill(array, []);
        foreach (var (block, targets) in edges) array[block] = targets.IsDefault ? [] : targets;
        return new(array);
    }

    public bool ContainsKey(int block) => (uint)block < (uint)_edges.Length;

    public bool TryGetValue(int block, [MaybeNullWhen(false)] out ImmutableArray<int> value)
    {
        if ((uint)block < (uint)_edges.Length)
        {
            value = _edges[block];
            return true;
        }
        value = default;
        return false;
    }

    // A copy with one block's edges replaced.
    public BlockEdgeDictionary SetItem(int block, ImmutableArray<int> targets)
    {
        var array = (ImmutableArray<int>[])_edges.Clone();
        array[block] = targets;
        return new(array);
    }

    // A copy with a new block's edges added.
    public BlockEdgeDictionary Add(int block, ImmutableArray<int> targets)
    {
        if (ContainsKey(block))
            throw new ArgumentException($"block {block} already has edges", nameof(block));
        var array = new ImmutableArray<int>[block + 1];
        Array.Fill(array, []);
        _edges.CopyTo(array, 0);
        array[block] = targets;
        return new(array);
    }

    // A copy of the edges to change before building a new graph.
    internal ImmutableArray<int>[] ToArray() => (ImmutableArray<int>[])_edges.Clone();

    public IEnumerator<KeyValuePair<int, ImmutableArray<int>>> GetEnumerator()
    {
        for (var block = 0; block < _edges.Length; block++) yield return new(block, _edges[block]);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed record ControlFlowGraph
{
    private readonly Dictionary<int, BasicBlock> _blocksByOffset;

    internal ControlFlowGraph(
        CilMethodBody methodBody,
        ImmutableArray<BasicBlock> blocks,
        IReadOnlyDictionary<int, ImmutableArray<int>> successors,
        IReadOnlyDictionary<int, ImmutableArray<int>> predecessors,
        IReadOnlyDictionary<int, ImmutableArray<int>> exceptionalSuccessors,
        IReadOnlyDictionary<int, ImmutableArray<int>> exceptionalPredecessors,
        ImmutableHashSet<int> reachableBlocks)
    {
        MethodBody = methodBody;
        Blocks = blocks;
        Successors = BlockEdgeDictionary.From(successors, blocks.Length);
        Predecessors = BlockEdgeDictionary.From(predecessors, blocks.Length);
        ExceptionalSuccessors = BlockEdgeDictionary.From(exceptionalSuccessors, blocks.Length);
        ExceptionalPredecessors = BlockEdgeDictionary.From(exceptionalPredecessors, blocks.Length);
        ReachableBlocks = reachableBlocks;
        _blocksByOffset = new(blocks.Length);
        foreach (var block in blocks) _blocksByOffset.Add(block.StartOffset, block);
    }

    public CilMethodBody MethodBody { get; }
    public ImmutableArray<BasicBlock> Blocks { get; }
    public BlockEdgeDictionary Successors { get; }
    public BlockEdgeDictionary Predecessors { get; }
    public BlockEdgeDictionary ExceptionalSuccessors { get; }
    public BlockEdgeDictionary ExceptionalPredecessors { get; }
    public ImmutableHashSet<int> ReachableBlocks { get; }
    public BasicBlock Entry => Blocks[0];

    public BasicBlock GetBlock(int index) => Blocks[index];
    public BasicBlock GetBlockAtOffset(int offset) => _blocksByOffset[offset];
}
