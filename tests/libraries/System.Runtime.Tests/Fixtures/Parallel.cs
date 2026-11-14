// From the compiler harness's ParallelCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace ParallelFixture;

public static class EntryPoint
{
    public static int Run(int input)
    {
        // Every index runs once.
        var seen = new int[100];
        var completed = Parallel.For(0, 100, i => Interlocked.Increment(ref seen[i]));
        if (!completed.IsCompleted || completed.LowestBreakIteration != null || seen.Any(count => count != 1)) return 1;

        // Thread-local state: each worker's total, combined in localFinally.
        long total = 0;
        Parallel.For(0L, 1000L, () => 0L, (i, state, local) => local + i, local => Interlocked.Add(ref total, local));
        if (total != 499500) return 2;

        // Break: every iteration below the break runs, and the result reports it.
        var below = new int[50];
        var broken = Parallel.For(0, 50, (i, state) =>
        {
            if (i == 20) state.Break();
            else if (i < 20) Interlocked.Increment(ref below[i]);
        });
        if (broken.IsCompleted || broken.LowestBreakIteration != 20) return 3;
        if (below.Take(20).Any(count => count != 1)) return 4;

        // Stop, and the loop state seen from inside.
        var flags = 0;
        var stopped = Parallel.For(0, 1000, (i, state) =>
        {
            if (i != 0) return;
            state.Stop();
            if (state.IsStopped && state.ShouldExitCurrentIteration && !state.IsExceptional && state.LowestBreakIteration == null) flags = 1;
        });
        if (stopped.IsCompleted || stopped.LowestBreakIteration != null || flags != 1) return 5;
        try
        {
            Parallel.For(0, 1, (i, state) => { state.Stop(); state.Break(); });
            return 6;
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException) { }

        // A body's exception ends the loop and surfaces aggregated.
        try
        {
            Parallel.For(0, 10, i => { if (i == 3) throw new InvalidOperationException("three"); });
            return 7;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count >= 1 && ex.InnerExceptions.All(e => e.Message == "three")) { }

        // ForEach over an enumerable, with each item's index.
        var bag = new ConcurrentBag<long>();
        Parallel.ForEach(Enumerable.Range(10, 5), (item, state, index) => bag.Add(item * 100 + index));
        if (bag.Count != 5 || bag.Sum() != 6010) return 8;

        // ForEach over a range partitioner.
        long ranges = 0;
        Parallel.ForEach(Partitioner.Create(0, 100, 10), range =>
        {
            for (var i = range.Item1; i < range.Item2; i++) Interlocked.Add(ref ranges, i);
        });
        if (ranges != 4950) return 9;

        // Invoke runs every action, a few directly and more than ten through the replicator.
        int a = 0, b = 0, c = 0;
        Parallel.Invoke(() => a = 1, () => b = 2, () => c = 3);
        if (a + b + c != 6) return 10;
        var invoked = 0;
        Parallel.Invoke(Enumerable.Range(0, 12).Select(_ => (Action)(() => Interlocked.Increment(ref invoked))).ToArray());
        if (invoked != 12) return 11;

        // Options: a canceled token, and one worker running the iterations in order.
        try
        {
            Parallel.For(0, 10, new ParallelOptions { CancellationToken = new CancellationToken(true) }, i => { });
            return 12;
        }
        catch (OperationCanceledException) { }
        var order = new List<int>();
        Parallel.For(0, 10, new ParallelOptions { MaxDegreeOfParallelism = 1 }, order.Add);
        if (!order.SequenceEqual(Enumerable.Range(0, 10))) return 13;

        // Argument validation, as upstream.
        try { Parallel.For(0, 1, (Action<int>)null!); return 14; } catch (ArgumentNullException) { }
        try { Parallel.Invoke((Action)null!); return 15; } catch (ArgumentException) { }
        return input + 1;
    }
}
