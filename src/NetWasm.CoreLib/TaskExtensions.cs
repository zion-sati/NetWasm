// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.Tasks
{
    public static class TaskExtensions
    {
        public static async Task Unwrap(this Task<Task> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            var inner = await task.ConfigureAwait(false);
            if (inner is null)
            {
                await Task.FromCanceled(new Threading.CancellationToken(canceled: true))
                    .ConfigureAwait(false);
                return;
            }
            await inner.ConfigureAwait(false);
        }

        public static async Task<TResult> Unwrap<TResult>(
            this Task<Task<TResult>> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            var inner = await task.ConfigureAwait(false);
            if (inner is null)
            {
                return await Task.FromCanceled<TResult>(
                        new Threading.CancellationToken(canceled: true))
                    .ConfigureAwait(false);
            }
            return await inner.ConfigureAwait(false);
        }
    }
}
