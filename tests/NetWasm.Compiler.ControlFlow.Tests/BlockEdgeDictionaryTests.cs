using System.Collections;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Tests;

// A graph's edges by dense block index: an array read as the map it stands for.
public sealed class BlockEdgeDictionaryTests
{
    private static readonly BlockEdgeDictionary Edges = BlockEdgeDictionary.From(
        new Dictionary<int, ImmutableArray<int>> { [0] = [1, 2], [2] = default }, blockCount: 3);

    [Fact]
    public void ItReadsAsAMapOfEveryBlock()
    {
        Assert.Equal([1, 2], Edges[0]);
        Assert.Empty(Edges[1]);
        Assert.Empty(Edges[2]);
        Assert.Throws<KeyNotFoundException>(() => Edges[3]);
        Assert.True(Edges.TryGetValue(0, out var first) && first.Length == 2);
        Assert.False(Edges.TryGetValue(-1, out _));
        Assert.Equal([0, 1, 2], Edges.Keys);
        Assert.Equal([0, 1, 2], Edges.Select(pair => pair.Key));
        Assert.Equal(3, ((IEnumerable)Edges).Cast<object>().Count());
    }

    [Fact]
    public void ABlockIsAddedOnceAndMapsOfOtherShapesAreDensified()
    {
        var added = Edges.Add(4, [0]);

        Assert.Equal([0], added[4]);
        Assert.Empty(added[3]);
        Assert.Throws<ArgumentException>(() => added.Add(4, [1]));
        Assert.Same(Edges, BlockEdgeDictionary.From(Edges, 3));
        Assert.Equal(5, BlockEdgeDictionary.From(Edges, 5).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockEdgeDictionary.From(new Dictionary<int, ImmutableArray<int>> { [-1] = [] }, 1));
    }
}
