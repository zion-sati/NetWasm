// Ported from dotnet/runtime src/tests/async/mincallcost-microbench/mincallcost-microbench.cs at
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce (v11.0.0): each benchmark is one pass of the test's inner loop (ten calls)
// instead of a loop over a timed window, and the methods are named for what they compile to.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace NetWasm.Workloads.Async
{
    // The cost of a call to an async method that completes without suspending, by how the caller and the callee are
    // compiled. "Async" methods are plain async methods, compiled as the runtime compiles them by default: runtime
    // async on NetWasm and on desktop .NET with runtime async on, state machines on Mono AOT, which has no runtime
    // async. "StateMachine" methods opt out of runtime async ([RuntimeAsyncMethodGeneration(false)]), so every runtime
    // compiles them as Roslyn's state machines. Each benchmark makes ten calls.
    public class CallCost
    {
        // Never zero: the callees' awaits are there to make them real async methods.
        private static double s_time = 10.0;

        [Benchmark]
        public async Task AsyncCallingAsync()
        {
            for (int i = 0; i < 10; i++) await EmptyAsync();
        }

        [Benchmark]
        public async Task AsyncCallingAsyncNoInlining()
        {
            for (int i = 0; i < 10; i++) await EmptyAsyncNoInlining();
        }

        // The callee saves and restores a stand-in for the thread's contexts, as a state machine's builder does.
        [Benchmark]
        public async Task AsyncCallingAsyncWithContextSave()
        {
            if (CurrentThread == null) CurrentThread = new FakeThread();
            for (int i = 0; i < 10; i++) await EmptyAsyncWithContextSave();
        }

        [Benchmark]
        public async Task AsyncCallingStateMachine()
        {
            for (int i = 0; i < 10; i++) await EmptyStateMachine();
        }

        [Benchmark]
        public async Task AsyncCallingValueTaskStateMachine()
        {
            for (int i = 0; i < 10; i++) await EmptyValueTaskStateMachine();
        }

        [Benchmark]
        public async Task AsyncCallingYield()
        {
            for (int i = 0; i < 10; i++) await Task.Yield();
        }

        [Benchmark]
        [RuntimeAsyncMethodGeneration(false)]
        public async Task StateMachineCallingAsync()
        {
            for (int i = 0; i < 10; i++) await EmptyAsync();
        }

        [Benchmark]
        [RuntimeAsyncMethodGeneration(false)]
        public async Task StateMachineCallingStateMachine()
        {
            for (int i = 0; i < 10; i++) await EmptyStateMachine();
        }

        [Benchmark]
        [RuntimeAsyncMethodGeneration(false)]
        public async Task StateMachineCallingValueTaskStateMachine()
        {
            for (int i = 0; i < 10; i++) await EmptyValueTaskStateMachine();
        }

        [Benchmark]
        [RuntimeAsyncMethodGeneration(false)]
        public async Task StateMachineCallingYield()
        {
            for (int i = 0; i < 10; i++) await Task.Yield();
        }

        // Ten calls of an empty method that is not inlined: the floor the async calls are measured against.
        [Benchmark]
        public long SyncCallingSync()
        {
            for (int i = 0; i < 10; i++) EmptyMethod();
            return 10;
        }

        [RuntimeAsyncMethodGeneration(false)]
        private static async Task EmptyStateMachine()
        {
            // Add some work that forces the method to be a real async method
            if (s_time == 0)
                await Task.Yield();
            return;
        }

        [RuntimeAsyncMethodGeneration(false)]
        private static async ValueTask EmptyValueTaskStateMachine()
        {
            // Add some work that forces the method to be a real async method
            if (s_time == 0)
                await Task.Yield();
            return;
        }

        private static async Task EmptyAsync()
        {
            // Add some work that forces the method to be a real async method
            if (s_time == 0)
                await Task.Yield();
            return;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task EmptyAsyncNoInlining()
        {
            // Add some work that forces the method to be a real async method
            if (s_time == 0)
                await Task.Yield();
            return;
        }

        // This simulates async capturing the same amount of state that existing async needs to capture to handle the
        // current semantics around async locals and synchronizationcontext
        private static async Task EmptyAsyncWithContextSave()
        {
            FakeThread thread = CurrentThread;
            FakeExecContext previousExecutionCtx = thread._execContext;
            FakeSyncContext previousSyncCtx = thread._syncContext;

            try
            {
                // Add some work that forces the method to be a real async method
                if (s_time == 0)
                    await Task.Yield();
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EmptyMethod()
        {
            return;
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

        [System.ThreadStatic]
        public static FakeThread CurrentThread;
    }
}
