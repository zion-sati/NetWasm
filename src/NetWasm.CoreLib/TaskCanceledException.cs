// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;

namespace System.Threading.Tasks
{
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class TaskCanceledException : OperationCanceledException
    {
        // Retain the standard constructor for derived-library compatibility.
        [System.Obsolete(System.Obsoletions.LegacyFormatterImplMessage, DiagnosticId = System.Obsoletions.LegacyFormatterImplDiagId, UrlFormat = System.Obsoletions.SharedUrlFormat)]
        protected TaskCanceledException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) =>
            throw new System.PlatformNotSupportedException("TaskCanceledException formatter deserialization is not available on this NetWasm platform.");

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
