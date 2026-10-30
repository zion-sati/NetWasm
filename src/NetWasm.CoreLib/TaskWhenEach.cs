// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks.Sources;

namespace System.Threading.Tasks
{
    public partial class Task
    {
        public static IAsyncEnumerable<Task> WhenEach(params Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            return WhenEachState.Iterate<Task>(
                WhenEachState.Create(MaterializeTasks(tasks)));
        }

        public static IAsyncEnumerable<Task> WhenEach(params ReadOnlySpan<Task> tasks)
        {
            var materialized = new Task[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index]
                    ?? throw new ArgumentException(nameof(tasks));
            }

            return WhenEachState.Iterate<Task>(WhenEachState.Create(materialized));
        }

        public static IAsyncEnumerable<Task> WhenEach(IEnumerable<Task> tasks) =>
            WhenEachState.Iterate<Task>(WhenEachState.Create(MaterializeTasks(tasks)));

        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(
            params Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            return WhenEachState.Iterate<Task<TResult>>(
                WhenEachState.Create(MaterializeTasks(tasks)));
        }

        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(
            params ReadOnlySpan<Task<TResult>> tasks)
        {
            var materialized = new Task<TResult>[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index]
                    ?? throw new ArgumentException(nameof(tasks));
            }

            return WhenEachState.Iterate<Task<TResult>>(
                WhenEachState.Create(materialized));
        }

        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(
            IEnumerable<Task<TResult>> tasks) =>
            WhenEachState.Iterate<Task<TResult>>(
                WhenEachState.Create(MaterializeTasks(tasks)));

        private sealed class WhenEachState : Queue<Task>, IValueTaskSource
        {
            private ManualResetValueTaskSourceCore<bool> _waitForNextCompletedTask =
                new() { RunContinuationsAsynchronously = true };
            private bool _enumerated;

            internal int Remaining { get; private set; }

            internal bool TryStart()
            {
                if (_enumerated)
                {
                    return false;
                }

                _enumerated = true;
                return true;
            }

            internal static WhenEachState? Create<TTask>(TTask[] tasks)
                where TTask : Task
            {
                if (tasks.Length == 0)
                {
                    return null;
                }

                var state = new WhenEachState
                {
                    Remaining = tasks.Length,
                };
                var continuation = new WhenEachContinuation(state);
                foreach (var task in tasks)
                {
                    task.RegisterContinuation(
                        continuation,
                        runInlineIfCompleted: true);
                }

                return state;
            }

            internal void EnqueueCompletion(Task task)
            {
                Enqueue(task);
                if (Count == 1)
                {
                    _waitForNextCompletedTask.SetResult(default);
                }
            }

            void IValueTaskSource.GetResult(short token) =>
                _waitForNextCompletedTask.GetResult(token);

            ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) =>
                _waitForNextCompletedTask.GetStatus(token);

            void IValueTaskSource.OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) =>
                _waitForNextCompletedTask.OnCompleted(
                    continuation,
                    state,
                    token,
                    flags);

            internal static async IAsyncEnumerable<TTask> Iterate<TTask>(
                WhenEachState? state,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
                where TTask : Task
            {
                if (state?.TryStart() is not true)
                {
                    yield break;
                }

                while (state.Remaining > 0)
                {
                    state._waitForNextCompletedTask.Reset();
                    if (state.TryDequeue(out var next))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        state.Remaining--;
                        yield return (TTask)next;
                        continue;
                    }

                    ValueTask waitTask = new(
                        state,
                        state._waitForNextCompletedTask.Version);
                    if (cancellationToken.CanBeCanceled && !waitTask.IsCompleted)
                    {
                        waitTask = new ValueTask(
                            waitTask.AsTask().WaitAsync(cancellationToken));
                    }

                    await waitTask.ConfigureAwait(false);
                }
            }
        }

        private sealed class WhenEachContinuation(WhenEachState state) :
            TaskContinuation
        {
            internal override void Run(Task antecedent, bool allowInline) =>
                state.EnqueueCompletion(antecedent);
        }
    }
}
