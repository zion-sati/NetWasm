// From the compiler harness's TaskWaitInliningCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
using System.Threading.Tasks;
namespace TaskWaitInliningFixture;

public static class EntryPoint
{
    public static int Run(int input)
    {
        var stage = 0;
        try
        {
            return Check(input, ref stage);
        }
        catch (PlatformNotSupportedException)
        {
            // A wait that should have completed by running its task inline would have blocked instead.
            return 100 + stage;
        }
    }

    static int Check(int input, ref int stage)
    {
        var ran = 0;
        stage = 1;
        if (Task.Run(() => { Interlocked.Increment(ref ran); return 40; }).Result != 40 || ran != 1) return 1;

        stage = 2;
        var started = Task.Factory.StartNew(() => Interlocked.Increment(ref ran));
        started.Wait();
        if (ran != 2 || started.Status != TaskStatus.RanToCompletion) return 2;

        stage = 3;
        Task.Run(() => { Interlocked.Increment(ref ran); }).GetAwaiter().GetResult();
        if (ran != 3) return 3;

        stage = 4;
        Task.WaitAll(Task.Run(() => Interlocked.Increment(ref ran)), Task.Run(() => Interlocked.Increment(ref ran)));
        if (ran != 5) return 4;

        stage = 5;
        // A fault surfaces from the wait as on desktop.
        try
        {
            Task.Run((Action)(() => throw new InvalidOperationException("inside"))).Wait();
            return 5;
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException { Message: "inside" })
        {
        }

#if !DESKTOP_ORACLE
        // Task.Run over an async delegate returns an unwrap promise, which has no work of its own to run inline:
        // its outer task is still queued, so the wait would block and throws, as upstream's single-threaded
        // builds do (desktop's pool runs the outer task meanwhile). Awaiting it is the one-thread way.
        stage = 6;
        try
        {
            Task.Run(() => Task.FromResult(2)).Wait();
            return 6;
        }
        catch (PlatformNotSupportedException)
        {
        }
#endif

        stage = 8;
        // A precanceled token: the task is canceled when it is dispatched, and the wait reports it.
        try
        {
            Task.Run(() => Interlocked.Increment(ref ran), new CancellationToken(true)).Wait();
            return 7;
        }
        catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
        {
        }
        if (ran != 5) return 8;
        return input + 1;
    }
}
