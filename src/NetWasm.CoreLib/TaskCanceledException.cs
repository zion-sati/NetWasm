// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;

namespace System.Threading.Tasks
{
    public class TaskCanceledException : OperationCanceledException
    {
        private readonly Task? _task;

        public TaskCanceledException()
            : base("A task was canceled.")
        {
        }

        public TaskCanceledException(string? message)
            : base(message ?? "A task was canceled.")
        {
        }

        public TaskCanceledException(string? message, Exception? innerException)
            : base(message ?? "A task was canceled.", innerException)
        {
        }

        public TaskCanceledException(
            string? message,
            Exception? innerException,
            CancellationToken token)
            : base(message ?? "A task was canceled.", innerException, token)
        {
        }

        public TaskCanceledException(Task? task)
            : base(
                "A task was canceled.",
                task is null ? CancellationToken.None : task.CancellationToken)
        {
            _task = task;
        }

        public Task? Task => _task;
    }
}
