using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

// Each path of the progress wait, driven by a scripted condition rather than by thread timing.
public sealed class ProgressWaitTests
{
    [Fact]
    public void AConditionThatHoldsReturnsAtOnce()
    {
        using var progress = new ManualResetEventSlim(true);
        var looks = 0;

        ProgressWait.Until(progress, 0, _ => ++looks > 0);

        Assert.Equal(1, looks);
        Assert.True(progress.IsSet);
    }

    // A completion between the first look and the reset is seen by the second look, without waiting
    // on an event nothing will set again.
    [Fact]
    public async Task ACompletionBeforeTheResetIsSeenWithoutWaiting()
    {
        using var progress = new ManualResetEventSlim(true);
        var looks = 0;

        var wait = Task.Run(() => ProgressWait.Until(progress, 0, _ => ++looks == 2), TestContext.Current.CancellationToken);

        await wait.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(2, looks);
        Assert.False(progress.IsSet);
    }

    // Otherwise it waits for the event, then looks again.
    [Fact]
    public async Task ALaterCompletionWakesTheWait()
    {
        using var progress = new ManualResetEventSlim(false);
        using var waiting = new SemaphoreSlim(0);
        var looks = 0;

        var wait = Task.Run(() => ProgressWait.Until(progress, 0, _ =>
        {
            if (++looks == 2)
            {
                waiting.Release();
            }
            return looks == 3;
        }), TestContext.Current.CancellationToken);
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        progress.Set();

        await wait.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(3, looks);
    }
}
