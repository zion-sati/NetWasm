using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

// Async state machines and Task, single-threaded, modelled on dotnet/performance's Perf_AsyncMethods and
// ValueTaskPerfTest. Each scenario prints ASYNC|name|ticks|frequency|checksum, then GC|collections|allocated|total
// for the timed region. Completions come from queued work items or the event loop, never from another thread, so
// every runtime runs the same interleaving and the checksum is exact.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var count) || count <= 0) return 2;
        Func<int, Task<long>>? scenario = args[0] switch
        {
            "async-completed" => CompletedTasks,
            "async-valuetask-completed" => CompletedValueTasks,
            "async-chain" => Chains,
            "yield" => Yields,
            "value-task-source" => ValueTaskSources,
            "ping-pong" => PingPong,
            "async-iterator" => Iterate,
            "when-all" => WhenAlls,
            _ => null,
        };
        if (scenario is null) return 3;

        // Run the timed work untimed first, so a tiered JIT reaches its optimized code.
        for (var warmup = 0; warmup < 3; warmup++)
        {
            await scenario(count);
        }

        _ = Stopwatch.GetTimestamp();
        _ = GC.CollectionCount(0);
        _ = GC.GetTotalAllocatedBytes(true);
        var collectionsBefore = GC.CollectionCount(0);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        var checksum = await scenario(count);
        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        var collections = GC.CollectionCount(0) - collectionsBefore;

        Console.WriteLine(string.Join("|", "ASYNC", args[0],
            elapsed.ToString(), Stopwatch.Frequency.ToString(), checksum.ToString()));
        Console.WriteLine(string.Join("|", "GC", collections.ToString(),
            allocated.ToString(), GC.GetTotalMemory(false).ToString()));
        return allocatedBefore <= 0 ? 6 : 0;
    }

    // An async method that completes without suspending: the builder's synchronous path and the cached task.
    private static async Task<long> CompletedTasks(int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            sum += await CompletesSynchronously(index);
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> CompletesSynchronously(int value)
    {
        await Task.CompletedTask;
        return value & 7;
    }

    private static async Task<long> CompletedValueTasks(int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            sum += await CompletesSynchronouslyValueTask(index);
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async ValueTask<int> CompletesSynchronouslyValueTask(int value)
    {
        await default(ValueTask);
        return value * 3 + 1;
    }

    // Ten nested async calls that complete synchronously.
    private static async Task<long> Chains(int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            sum += await Chain(10, index);
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> Chain(int depth, int value) =>
        depth == 0 ? value & 15 : await Chain(depth - 1, value) + 1;

    // Suspends and resumes through the scheduler every iteration.
    private static async Task<long> Yields(int count)
    {
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            await Task.Yield();
            sum += index & 3;
        }

        return sum;
    }

    // A reusable IValueTaskSource, completed by a queued work item: the suspending ValueTask path without a Task.
    private static async Task<long> ValueTaskSources(int count)
    {
        var source = new ReusableSource();
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            sum += await source.Next(index);
        }

        return sum;
    }

    private sealed class ReusableSource : IValueTaskSource<int>, IThreadPoolWorkItem
    {
        private ManualResetValueTaskSourceCore<int> _core;
        private int _pending;

        public ValueTask<int> Next(int value)
        {
            _core.Reset();
            _pending = value;
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            return new ValueTask<int>(this, _core.Version);
        }

        public void Execute() => _core.SetResult(_pending % 11);

        public int GetResult(short token) => _core.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }

    // Two async loops handing a value back and forth through TaskCompletionSource.
    private static async Task<long> PingPong(int count)
    {
        var ping = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pong = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sources = new[] { ping, pong };
        var responder = Respond(sources, count);
        long sum = 0;
        for (var index = 0; index < count; index++)
        {
            var reply = sources[1];
            sources[0].SetResult(index);
            sum += await reply.Task;
        }

        await responder;
        return sum;
    }

    private static async Task Respond(TaskCompletionSource<int>[] sources, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var value = await sources[0].Task;
            sources[0] = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = sources[1];
            sources[1] = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            reply.SetResult(value ^ 5);
        }
    }

    // An async iterator that mostly completes synchronously and yields every 64th item.
    private static async Task<long> Iterate(int count)
    {
        long sum = 0;
        await foreach (var item in Items(count))
        {
            sum += item;
        }

        return sum;
    }

    private static async IAsyncEnumerable<int> Items(int count)
    {
        for (var index = 0; index < count; index++)
        {
            if ((index & 63) == 0)
            {
                await Task.Yield();
            }

            yield return index % 13;
        }
    }

    // WhenAll over eight methods that each suspend once.
    private static async Task<long> WhenAlls(int count)
    {
        long sum = 0;
        var tasks = new Task<int>[8];
        for (var round = 0; round < count; round++)
        {
            for (var index = 0; index < tasks.Length; index++)
            {
                tasks[index] = YieldsOnce(round + index);
            }

            var results = await Task.WhenAll(tasks);
            for (var index = 0; index < results.Length; index++)
            {
                sum += results[index];
            }
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> YieldsOnce(int value)
    {
        await Task.Yield();
        return value % 7;
    }
}
