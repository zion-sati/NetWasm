// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
        if (workers.Count == 1)
        {
            // One worker works on the calling thread, as hosts without threads require.
            for (var requestIndex = 0; requestIndex < requestCount; requestIndex++)
                results[requestIndex] = work(workers[0], requestIndex);
            return results;
        }
        var failures = new ExceptionDispatchInfo?[requestCount];
        var next = -1;
        // Large-stack threads: a pass recurses once per nesting level of the method it works on.
        LargeStackThreads.Run(Math.Min(workers.Count, requestCount), index =>
        {
            var worker = workers[index];
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
        foreach (var failure in failures)
            failure?.Throw();
        return results;
    }
}
