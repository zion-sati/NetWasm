// Ported from dotnet/runtime src/tests/async/eh-microbench/eh-microbench.cs at
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (v11.0.0): its command-line settings are [Params], and each benchmark is
// one call from the top frame, whose catch the test's timed loop held, instead of a loop over a timed window.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace NetWasm.Workloads.Async
{
    // An exception, or a value, comes back up a chain of Depth async methods. The innermost yields Yields times first;
    // every FinallyEvery-th method has a try/finally; Throw chooses between throwing and returning. "Async" compiles
    // as the runtime compiles async methods by default (runtime async on NetWasm and on desktop .NET with runtime
    // async on, state machines on Mono AOT); "StateMachine" opts out of runtime async on every runtime.
    public class ExceptionPropagation
    {
        [Params(2, 32)]
        public int Depth;

        [Params(0, 1)]
        public int Yields;

        [Params(1, 1000)]
        public int FinallyEvery;

        [Params(false, true)]
        public bool Throw;

        public int Sink;

        [Benchmark]
        public async Task<long> Async()
        {
            try
            {
                return await RunAsync(Depth - 1);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        // Each method saves and restores a stand-in for the thread's contexts, as a state machine's builder does.
        [Benchmark]
        public async Task<long> AsyncWithContextSaveRestore()
        {
            try
            {
                return await RunAsyncWithContextSaveRestore(Depth - 1);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [Benchmark]
        [RuntimeAsyncMethodGeneration(false)]
        public async Task<long> StateMachineTask()
        {
            try
            {
                return await RunTask(Depth - 1);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [Benchmark]
        [RuntimeAsyncMethodGeneration(false)]
        public async Task<long> StateMachineValueTask()
        {
            try
            {
                return await RunValueTask(Depth - 1);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        [RuntimeAsyncMethodGeneration(false)]
        private async Task<long> RunTask(int depth)
        {
            int liveState1 = depth * 3 + Yields;
            int liveState2 = depth;
            double liveState3 = Yields;
            if (depth == 0)
            {
                int currentAwaitCount = 0;
                while (currentAwaitCount < Yields)
                {
                    currentAwaitCount++;
                    await Task.Yield();
                }

                if (Throw)
                    throw new Exception();

                return 8375983;
            }

            long result = 0;
            if ((depth % FinallyEvery) == 0)
            {
                try
                {
                    result = await RunTask(depth - 1);
                }
                finally
                {
                    Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
                }
            }
            else
            {
                result = await RunTask(depth - 1);
            }

            Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
            return result;
        }

        [RuntimeAsyncMethodGeneration(false)]
        private async ValueTask<long> RunValueTask(int depth)
        {
            int liveState1 = depth * 3 + Yields;
            int liveState2 = depth;
            double liveState3 = Yields;
            if (depth == 0)
            {
                int currentAwaitCount = 0;
                while (currentAwaitCount < Yields)
                {
                    currentAwaitCount++;
                    await Task.Yield();
                }

                if (Throw)
                    throw new Exception();

                return 8375983;
            }

            long result = 0;
            if ((depth % FinallyEvery) == 0)
            {
                try
                {
                    result = await RunValueTask(depth - 1);
                }
                finally
                {
                    Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
                }
            }
            else
            {
                result = await RunValueTask(depth - 1);
            }

            Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
            return result;
        }

        public class FakeSyncContext {}

        public class FakeExecContext
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void RestoreChangedContextToThread(FakeThread thread, FakeExecContext execContext, FakeExecContext newExecContext)
            {
            }
        }

        public class FakeThread
        {
            public FakeSyncContext _syncContext;
            public FakeExecContext _execContext;
        }

        [ThreadStatic]
        public static FakeThread CurrentThread;

        // This case is used to test the impact of save/restore of the sync and execution context on performance
        // The intent here is to measure what the performance impact of maintaining the current async semantics with
        // the new implementation.
        private async Task<long> RunAsyncWithContextSaveRestore(int depth)
        {
            FakeThread thread = CurrentThread;
            if (thread == null)
            {
                CurrentThread = new FakeThread();
                thread = CurrentThread;
            }

            FakeExecContext previousExecutionCtx = thread._execContext;
            FakeSyncContext previousSyncCtx = thread._syncContext;

            try
            {
                int liveState1 = depth * 3 + Yields;
                int liveState2 = depth;
                double liveState3 = Yields;
                if (depth == 0)
                {
                    int currentAwaitCount = 0;
                    while (currentAwaitCount < Yields)
                    {
                        currentAwaitCount++;
                        await Task.Yield();
                    }

                    if (Throw)
                        throw new Exception();

                    return 8375983;
                }

                long result = 0;
                if ((depth % FinallyEvery) == 0)
                {
                    try
                    {
                        result = await RunAsyncWithContextSaveRestore(depth - 1);
                    }
                    finally
                    {
                        Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
                    }
                }
                else
                {
                    result = await RunAsyncWithContextSaveRestore(depth - 1);
                }

                Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
                return result;
            }
            finally
            {
                // The common case is that these have not changed, so avoid the cost of a write barrier if not needed.
                if (previousSyncCtx != thread._syncContext)
                {
                    // Restore changed SynchronizationContext back to previous
                    thread._syncContext = previousSyncCtx;
                }

                FakeExecContext currentExecutionCtx = thread._execContext;
                if (previousExecutionCtx != currentExecutionCtx)
                {
                    FakeExecContext.RestoreChangedContextToThread(thread, previousExecutionCtx, currentExecutionCtx);
                }
            }
        }

        private async Task<long> RunAsync(int depth)
        {
            int liveState1 = depth * 3 + Yields;
            int liveState2 = depth;
            double liveState3 = Yields;
            if (depth == 0)
            {
                int currentAwaitCount = 0;
                while (currentAwaitCount < Yields)
                {
                    currentAwaitCount++;
                    await Task.Yield();
                }

                if (Throw)
                    throw new Exception();

                return 8375983;
            }

            long result = 0;
            if ((depth % FinallyEvery) == 0)
            {
                try
                {
                    result = await RunAsync(depth - 1);
                }
                finally
                {
                    Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
                }
            }
            else
            {
                result = await RunAsync(depth - 1);
            }

            Sink = (int)liveState1 + (int)liveState2 + (int)(1 / liveState3) + depth;
            return result;
        }
    }
}
