using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class IndexedWorkExecutorTests
{
    [Fact]
    public void ReturnsResultsInRequestOrderAndAcceptsEmptyBatch()
    {
        var executor = Create();
        var results = executor.Execute([0, 1, 2, 3], 16,
            (worker, index) => index * 10);

        Assert.Equal(Enumerable.Range(0, 16).Select(index => index * 10),
            results);
        Assert.Empty(executor.Execute([0], 0, (worker, index) => index));
    }

    [Fact]
    public void JoinsAllWorkersAndReportsEarliestRequestFailure()
    {
        var executor = Create();
        using var laterFailure = new ManualResetEventSlim();
        var completed = 0;

        var error = Assert.Throws<InvalidOperationException>(() =>
            executor.Execute([0, 1, 2, 3], 8, (worker, index) =>
            {
                try
                {
                    if (index == 1)
                    {
                        Assert.True(laterFailure.Wait(TimeSpan.FromSeconds(5)));
                        throw new InvalidOperationException("first in request order");
                    }
                    if (index == 4)
                    {
                        laterFailure.Set();
                        throw new InvalidOperationException("later in request order");
                    }
                    return index;
                }
                finally { Interlocked.Increment(ref completed); }
            }));

        Assert.Equal("first in request order", error.Message);
        Assert.Equal(8, completed);
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        var executor = Create();
        Assert.Throws<ArgumentNullException>(() =>
            executor.Execute<int, int>(null!, 1, (worker, index) => index));
        Assert.Throws<ArgumentNullException>(() =>
            executor.Execute<int, int>([0], 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            executor.Execute([0], -1, (worker, index) => index));
        Assert.Throws<ArgumentException>(() =>
            executor.Execute(Array.Empty<int>(), 1,
                (worker, index) => index));
    }

    [Theory]
    [InlineData(true, 16, 1)]
    [InlineData(false, 0, 1)]
    [InlineData(false, 2, 2)]
    [InlineData(false, 6, 6)]
    [InlineData(false, 16, 8)]
    public void HostPolicyCapsNativeWorkersAndKeepsBrowserSerial(
        bool browser, int processors, int expected)
    {
        Assert.Equal(expected,
            CompilerParallelism.ForHost(browser, processors).WorkerCount);
    }

    // This test intentionally exercises the injected capability contract.
#pragma warning disable CA1859
    private static IIndexedWorkExecutor Create() => new IndexedWorkExecutor();
#pragma warning restore CA1859
}
