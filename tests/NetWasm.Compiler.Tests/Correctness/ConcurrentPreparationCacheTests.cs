namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ConcurrentPreparationCacheTests
{
    [Fact]
    public async Task ConcurrentCallersPrepareEachKeyExactlyOnce()
    {
        var cache = new ConcurrentPreparationCache<string, object>(StringComparer.Ordinal);
        var preparationCount = 0;
        using var callersReady = new CountdownEvent(10);
        using var start = new ManualResetEventSlim();
        using var preparationEntered = new ManualResetEventSlim();
        using var releasePreparation = new ManualResetEventSlim();

        var calls = Enumerable.Range(0, 10)
            .Select(_ => Task.Factory.StartNew(() =>
                {
                    callersReady.Signal();
                    start.Wait();
                    return cache.GetOrAdd("shared", _ =>
                    {
                        Interlocked.Increment(ref preparationCount);
                        preparationEntered.Set();
                        releasePreparation.Wait();
                        return new object();
                    });
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        Assert.True(callersReady.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        start.Set();
        Assert.True(preparationEntered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref preparationCount));
        releasePreparation.Set();
        var values = await Task.WhenAll(calls);

        Assert.Equal(1, preparationCount);
        Assert.All(values, value => Assert.Same(values[0], value));
    }

    [Fact]
    public void DifferentKeysArePreparedIndependently()
    {
        var cache = new ConcurrentPreparationCache<string, string>(StringComparer.Ordinal);

        Assert.Equal("first", cache.GetOrAdd("a", _ => "first"));
        Assert.Equal("second", cache.GetOrAdd("b", _ => "second"));
    }
}
