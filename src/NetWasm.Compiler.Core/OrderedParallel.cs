// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace NetWasm.Compiler.Core;

/// <summary>
/// Runs independent per-item work (validation, metrics) on all cores while behaving like a
/// loop over the items in order: results keep their positions, and when items fail, the
/// failure of the first failing item is the one thrown.
/// </summary>
public static class OrderedParallel
{
    // Below this many items the work runs in turn.
    private const int SerialThreshold = 64;

    public static void For(int count, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _ = Map(count, index =>
        {
            body(index);
            return true;
        });
    }

    public static TResult[] Map<TResult>(int count, Func<int, TResult> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var results = new TResult[count];
        // A host with one core, as a host without threads reports, runs the items in turn too.
        if (count < SerialThreshold || Environment.ProcessorCount == 1)
        {
            for (var index = 0; index < count; index++) results[index] = body(index);
            return results;
        }
        var failures = new ExceptionDispatchInfo?[count];
        var next = -1;
        // Large-stack threads, one per core, each taking the next item: a check recurses as deep as the
        // method it checks nests.
        LargeStackThreads.Run(Math.Min(Environment.ProcessorCount, count), _ =>
        {
            for (var index = Interlocked.Increment(ref next); index < count; index = Interlocked.Increment(ref next))
            {
                try
                {
                    results[index] = body(index);
                }
#pragma warning disable CA1031 // Rethrown below, in item order.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    failures[index] = ExceptionDispatchInfo.Capture(exception);
                }
            }
        });
        foreach (var failure in failures) failure?.Throw();
        return results;
    }
}
