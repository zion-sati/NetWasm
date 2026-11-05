namespace NetWasm.Compiler.Tests.Correctness;

// Runs alone: it caps the thread pool and occupies every thread of it.
[CollectionDefinition(nameof(ThreadPoolSaturation), DisableParallelization = true)]
public sealed class ThreadPoolSaturation;

// The test host runs many processes at once and blocks pool threads waiting on them. A process
// that exited must still be reported as exited, with its output, whatever the pool is doing.
[Collection(nameof(ThreadPoolSaturation))]
public sealed class QualifiedProcessRunnerStarvationTests
{
    [Fact]
    public void AnExitedProcessIsReadWhileEveryPoolThreadIsBusy()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The fixture process is a POSIX shell.");
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxCompletionPorts);
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        var release = new ManualResetEventSlim();
        var started = new int[1];
        var finished = new int[1];
        Assert.True(ThreadPool.SetMaxThreads(minWorkers, maxCompletionPorts));
        try
        {
            for (var index = 0; index < minWorkers; index++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    Interlocked.Increment(ref started[0]);
                    release.Wait();
                    Interlocked.Increment(ref finished[0]);
                }, null);
            }
            // The pool is saturated once no more of the items start while some still wait: this
            // test and its host hold threads of their own, so not every item gets one.
            var last = -1;
            var stable = System.Diagnostics.Stopwatch.StartNew();
            while (stable.Elapsed < TimeSpan.FromMilliseconds(500))
            {
                var now = Volatile.Read(ref started[0]);
                if (now != last)
                {
                    last = now;
                    stable.Restart();
                }
                Thread.Sleep(20);
            }
            Assert.True(ThreadPool.PendingWorkItemCount > 0);

            // A runner that waited on the pool would never return, so it runs on a thread of its
            // own and the test fails instead of hanging.
            QualifiedProcessResult? completed = null;
            var running = new Thread(() => completed = new QualifiedProcessRunner().Run(new(
                // The output comes after the reads start, so they cannot finish synchronously.
                "/bin/sh", ["-c", "sleep 0.3; printf out; printf err >&2"], TimeSpan.FromSeconds(10)),
                TestContext.Current.CancellationToken))
            {
                IsBackground = true,
            };
            running.Start();
            Assert.True(running.Join(TimeSpan.FromSeconds(20)), "The runner waited on the busy thread pool.");
            var result = completed!;

            Assert.Equal(QualifiedProcessCompletion.Exited, result.Completion);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("out", result.StandardOutput);
            Assert.Equal("err", result.StandardError);
        }
        finally
        {
            release.Set();
            ThreadPool.SetMaxThreads(maxWorkers, maxCompletionPorts);
            // The items the saturated pool left queued start after the release, possibly after
            // the test returns; one that waited on a disposed event would throw on a pool thread
            // and end the test process. The event is disposed only once every item has finished.
            if (SpinWait.SpinUntil(() => Volatile.Read(ref finished[0]) == minWorkers, TimeSpan.FromSeconds(30)))
            {
                release.Dispose();
            }
        }
    }
}
