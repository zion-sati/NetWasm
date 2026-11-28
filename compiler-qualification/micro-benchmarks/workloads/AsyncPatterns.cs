// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using BenchmarkDotNet.Attributes;

namespace NetWasm.Workloads.Async
{
    // The scenarios of NetWasm's async workload (compiler-qualification/runtime-performance/workloads/async), each a
    // benchmark of a fixed count of operations: plain async methods, compiled as each runtime compiles them by default.
    public class Patterns
    {
        [Benchmark]
        public Task<long> CompletedTask() => CompletedTasks(100);

        [Benchmark]
        public Task<long> CompletedValueTask() => CompletedValueTasks(100);

        // A hundred chains of ten nested async calls that complete without suspending.
        [Benchmark]
        public Task<long> Chain() => Chains(100);

        [Benchmark]
        public Task<long> Yield() => Yields(100);

        [Benchmark]
        public Task<long> YieldChain() => YieldChains(100);

        // Ten exceptions, each thrown ten async calls down and caught at the top.
        [Benchmark]
        public Task<long> ExceptionChain() => ExceptionChains(10, suspend: false);

        [Benchmark]
        public Task<long> ExceptionChainAfterYield() => ExceptionChains(10, suspend: true);

        [Benchmark]
        public Task<long> ValueTaskSource() => ValueTaskSources(100);

        [Benchmark]
        public Task<long> PingPong() => PingPongs(100);

        // 640 items, yielding every 64th.
        [Benchmark]
        public Task<long> AsyncIterator() => Iterate(640);

        // Ten rounds of WhenAll over eight methods that each suspend once.
        [Benchmark]
        public Task<long> WhenAll() => WhenAlls(10);

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

        // Two nested methods suspend once per iteration: a chain that suspends, not only its innermost method.
        private static async Task<long> YieldChains(int count)
        {
            long sum = 0;
            for (var index = 0; index < count; index++)
            {
                sum += await YieldOuter(index);
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<int> YieldOuter(int value) => await YieldInner(value) + 1;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<int> YieldInner(int value)
        {
            await Task.Yield();
            return value & 3;
        }

        // An exception thrown ten methods down and caught at the top, thrown at once or after the innermost method
        // suspends: each method between rethrows it at its await.
        private static async Task<long> ExceptionChains(int count, bool suspend)
        {
            long sum = 0;
            for (var index = 0; index < count; index++)
            {
                try
                {
                    sum += await Throwing(10, index, suspend);
                }
                catch (InvalidOperationException)
                {
                    sum += index & 7;
                }
            }

            return sum;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<int> Throwing(int depth, int value, bool suspend)
        {
            if (depth == 0)
            {
                if (suspend) await Task.Yield();
                throw new InvalidOperationException();
            }

            return await Throwing(depth - 1, value, suspend) + 1;
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
        private static async Task<long> PingPongs(int count)
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
}
