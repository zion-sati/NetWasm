using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Compiler.Core;

/// <summary>Runs private worker capabilities and returns results in request order.</summary>
public sealed class IndexedWorkExecutor : IIndexedWorkExecutor
{
    public TResult[] Execute<TWorker, TResult>(
        IReadOnlyList<TWorker> workers,
        int requestCount,
        Func<TWorker, int, TResult> work)
    {
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfNegative(requestCount);
        if (workers.Count == 0)
            throw new ArgumentException(
                "At least one worker is required.", nameof(workers));

        var results = new TResult[requestCount];
        if (requestCount == 0) return results;
        var failures = new ExceptionDispatchInfo?[requestCount];
        var next = -1;
        var tasks = new Task[Math.Min(workers.Count, requestCount)];
        for (var index = 0; index < tasks.Length; index++)
        {
            var worker = workers[index];
            tasks[index] = Task.Run(() =>
            {
                while (true)
                {
                    var requestIndex = Interlocked.Increment(ref next);
                    if (requestIndex >= requestCount) return;
                    try { results[requestIndex] = work(worker, requestIndex); }
                    catch (Exception exception)
                    {
                        failures[requestIndex] = ExceptionDispatchInfo.Capture(
                            exception);
                    }
                }
            });
        }

        Task.WaitAll(tasks);
        foreach (var failure in failures)
            failure?.Throw();
        return results;
    }
}
