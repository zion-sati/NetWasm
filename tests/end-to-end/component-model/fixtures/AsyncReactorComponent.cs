using System;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.ComponentModel;

public static class AsyncReactorComponent
{
    private static Task<int>? _pending;

    [WitExport("netwasm:test-reactor/acceptance@1.0.0", "start")]
    public static bool Start(int value, uint delayMilliseconds)
    {
        if (_pending != null && !_pending.IsCompleted)
        {
            throw new InvalidOperationException();
        }

        _pending = CompleteAfterDelay(value, checked((int)delayMilliseconds));
        return !_pending.IsCompleted;
    }

    [WitExport("netwasm:test-reactor/acceptance@1.0.0", "start-faulted")]
    public static bool StartFaulted(uint delayMilliseconds)
    {
        if (_pending != null && !_pending.IsCompleted)
        {
            throw new InvalidOperationException();
        }

        _pending = FailAfterDelay(checked((int)delayMilliseconds));
        return !_pending.IsCompleted;
    }

    [WitExport("netwasm:test-reactor/acceptance@1.0.0", "is-completed")]
    public static bool IsCompleted() => _pending?.IsCompleted ?? false;

    [WitExport("netwasm:test-reactor/acceptance@1.0.0", "observe-failure")]
    public static bool ObserveFailure()
    {
        if (_pending == null || !_pending.IsCompleted)
        {
            throw new InvalidOperationException();
        }

        try
        {
            _ = _pending.Result;
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [WitExport("netwasm:test-reactor/acceptance@1.0.0", "result")]
    public static int Result()
    {
        if (_pending == null || !_pending.IsCompleted)
        {
            throw new InvalidOperationException();
        }

        return _pending.Result;
    }

    [WitExport("netwasm:test-reactor/acceptance@1.0.0", "start-cancelled")]
    public static bool StartCancelled(uint delayMilliseconds)
    {
        using var source = new CancellationTokenSource();
        var pending = Task.Delay(checked((int)delayMilliseconds), source.Token);
        source.Cancel();
        return pending.IsCanceled;
    }

    public static int Run(int value) => value;

    private static async Task<int> CompleteAfterDelay(int value, int delayMilliseconds)
    {
        await Task.Delay(delayMilliseconds);
        return checked(value + 1);
    }

    private static async Task<int> FailAfterDelay(int delayMilliseconds)
    {
        await Task.Delay(delayMilliseconds);
        throw new InvalidOperationException();
    }
}
