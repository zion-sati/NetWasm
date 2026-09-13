// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Threading.Tasks
{
    public partial class Task
    {
        public static IAsyncEnumerable<Task> WhenEach(params Task[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            return IterateCompleted(MaterializeTasks(tasks));
        }

        public static IAsyncEnumerable<Task> WhenEach(params ReadOnlySpan<Task> tasks)
        {
            var materialized = new Task[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index];
            }
            return WhenEach(materialized);
        }

        public static IAsyncEnumerable<Task> WhenEach(IEnumerable<Task> tasks) =>
            IterateCompleted(MaterializeTasks(tasks));

        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(
            params Task<TResult>[] tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            return IterateCompleted(MaterializeTasks(tasks));
        }

        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(
            params ReadOnlySpan<Task<TResult>> tasks)
        {
            var materialized = new Task<TResult>[tasks.Length];
            for (var index = 0; index < tasks.Length; index++)
            {
                materialized[index] = tasks[index];
            }
            return WhenEach(materialized);
        }

        public static IAsyncEnumerable<Task<TResult>> WhenEach<TResult>(
            IEnumerable<Task<TResult>> tasks) =>
            IterateCompleted(MaterializeTasks(tasks));

        private static async IAsyncEnumerable<TTask> IterateCompleted<TTask>(
            TTask[] tasks,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default(System.Threading.CancellationToken))
            where TTask : Task
        {
            var remaining = new List<TTask>(tasks.Length);
            foreach (var task in tasks)
            {
                remaining.Add(task ?? throw new ArgumentException(nameof(tasks)));
            }

            while (remaining.Count != 0)
            {
                var completedIndex = -1;
                for (var index = 0; index < remaining.Count; index++)
                {
                    if (remaining[index].IsCompleted)
                    {
                        completedIndex = index;
                        break;
                    }
                }

                TTask completed;
                if (completedIndex >= 0)
                {
                    completed = remaining[completedIndex];
                }
                else
                {
                    var completion = new TaskCompletionSource<TTask>();
                    foreach (var pending in remaining)
                    {
                        pending.OnCompleted(
                            () => completion.TrySetResult(pending),
                            continueOnCapturedContext: false);
                    }
                    completed = await completion.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                    completedIndex = remaining.IndexOf(completed);
                }

                remaining.RemoveAt(completedIndex);
                yield return completed;
            }
        }
    }
}
