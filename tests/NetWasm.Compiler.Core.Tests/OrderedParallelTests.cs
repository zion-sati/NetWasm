using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

// Work run on all cores behaves as a loop in order: results keep their positions, and of
// the items that fail, the first one's failure is thrown.
public sealed class OrderedParallelTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    public void ResultsKeepTheirPositions(int count) =>
        Assert.Equal(Enumerable.Range(0, count).Select(index => index * 2), OrderedParallel.Map(count, index => index * 2));

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    public void TheFirstFailingItemsFailureIsThrown(int count)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => OrderedParallel.For(count, index =>
        {
            if (index % 7 == 3) throw new InvalidOperationException($"item {index}");
        }));

        Assert.Equal("item 3", failure.Message);
    }
}
