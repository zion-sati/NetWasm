// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Runtime.InteropServices;

namespace System.Threading
{
    public delegate void SendOrPostCallback(object? state);

    /// <summary>Provides a single-thread callback dispatch boundary.</summary>
    public class SynchronizationContext
    {
        private static SynchronizationContext? s_current;
        private bool _requireWaitNotification;

        public SynchronizationContext()
        {
        }

        public static SynchronizationContext? Current => s_current;

        protected void SetWaitNotificationRequired() => _requireWaitNotification = true;

        public bool IsWaitNotificationRequired() => _requireWaitNotification;

        public virtual void Send(SendOrPostCallback callback, object? state)
        {
            if (callback == null)
            {
                throw new ArgumentNullException();
            }

            callback(state);
        }

        public virtual void Post(SendOrPostCallback callback, object? state)
        {
            if (callback == null)
            {
                throw new ArgumentNullException();
            }

            new PostedCallback(callback, state).Schedule();
        }

        public virtual void OperationStarted()
        {
        }

        public virtual void OperationCompleted()
        {
        }

        public static void SetSynchronizationContext(SynchronizationContext? context) =>
            s_current = context;

        public virtual SynchronizationContext CreateCopy() => new();

        private sealed class PostedCallback
        {
            private readonly SendOrPostCallback _callback;
            private readonly object? _state;
            private readonly ExecutionContext? _executionContext;
            private IPlatformSchedule? _schedule;

            internal PostedCallback(SendOrPostCallback callback, object? state)
            {
                _callback = callback;
                _state = state;
                _executionContext = ExecutionContext.Capture();
            }

            internal void Schedule() =>
                _schedule = PlatformServices.Scheduler.Schedule(Run, 0);

            private void Run()
            {
                _schedule?.Dispose();
                _schedule = null;
                if (_executionContext is null)
                {
                    _callback(_state);
                    return;
                }

                ExecutionContext.Run(
                    _executionContext,
                    static state => ((PostedCallback)state!).Invoke(),
                    this);
            }

            private void Invoke() => _callback(_state);
        }
    }
}
