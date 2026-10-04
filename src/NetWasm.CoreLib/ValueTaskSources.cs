// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Threading.Tasks.Sources
{
    public enum ValueTaskSourceOnCompletedFlags
    {
        None = 0,
        UseSchedulingContext = 1,
        FlowExecutionContext = 2,
    }

    public enum ValueTaskSourceStatus
    {
        Pending = 0,
        Succeeded = 1,
        Faulted = 2,
        Canceled = 3,
    }

    public interface IValueTaskSource
    {
        ValueTaskSourceStatus GetStatus(short token);
        void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags);
        void GetResult(short token);
    }

    public interface IValueTaskSource<out TResult>
    {
        ValueTaskSourceStatus GetStatus(short token);
        void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags);
        TResult GetResult(short token);
    }

    public struct ManualResetValueTaskSourceCore<TResult>
    {
        private Action<object?>? _continuation;
        private object? _continuationState;
        private Threading.SynchronizationContext? _synchronizationContext;
        private Threading.Tasks.TaskScheduler? _scheduler;
        private Threading.ExecutionContext? _executionContext;
        private Exception? _error;
        private TResult _result;
        private short _version;
        private bool _completed;

        public bool RunContinuationsAsynchronously { readonly get; set; }
        public short Version => _version;

        public void Reset()
        {
            _version = unchecked((short)(_version + 1));
            _continuation = null;
            _continuationState = null;
            _synchronizationContext = null;
            _scheduler = null;
            _executionContext = null;
            _error = null;
            _result = default!;
            _completed = false;
        }

        public void SetResult(TResult result)
        {
            _result = result;
            Complete();
        }

        public void SetException(Exception error)
        {
            _error = error ?? throw new ArgumentNullException();
            Complete();
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            ValidateToken(token);
            if (!_completed)
            {
                return ValueTaskSourceStatus.Pending;
            }
            if (_error is OperationCanceledException)
            {
                return ValueTaskSourceStatus.Canceled;
            }
            return _error == null
                ? ValueTaskSourceStatus.Succeeded
                : ValueTaskSourceStatus.Faulted;
        }

        public TResult GetResult(short token)
        {
            ValidateToken(token);
            if (!_completed)
            {
                throw new InvalidOperationException();
            }
            if (_error != null)
            {
                throw _error;
            }
            return _result;
        }

        public void OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }
            ValidateToken(token);
            if (_continuation != null)
            {
                throw new InvalidOperationException();
            }
            if (_completed)
            {
                CaptureSchedulingContext(
                    flags,
                    out var synchronizationContext,
                    out var scheduler);
                InvokeContinuation(
                    continuation,
                    state,
                    synchronizationContext,
                    scheduler,
                    (flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0
                        ? Threading.ExecutionContext.Capture()
                        : null);
                return;
            }
            _continuationState = state;
            CaptureSchedulingContext(
                flags,
                out _synchronizationContext,
                out _scheduler);
            _executionContext =
                (flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) != 0
                    ? Threading.ExecutionContext.Capture()
                    : null;
            _continuation = continuation;
        }

        private void Complete()
        {
            if (_completed)
            {
                throw new InvalidOperationException();
            }
            _completed = true;
            var continuation = _continuation;
            var state = _continuationState;
            var synchronizationContext = _synchronizationContext;
            var scheduler = _scheduler;
            var executionContext = _executionContext;
            _continuation = null;
            _continuationState = null;
            _synchronizationContext = null;
            _scheduler = null;
            _executionContext = null;
            if (continuation != null)
            {
                InvokeContinuation(
                    continuation,
                    state,
                    synchronizationContext,
                    scheduler,
                    executionContext);
            }
        }

        private static void CaptureSchedulingContext(
            ValueTaskSourceOnCompletedFlags flags,
            out Threading.SynchronizationContext? synchronizationContext,
            out Threading.Tasks.TaskScheduler? scheduler)
        {
            synchronizationContext = null;
            scheduler = null;
            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) == 0)
            {
                return;
            }

            var currentContext = Threading.SynchronizationContext.Current;
            if (currentContext is not null &&
                currentContext.GetType() != typeof(Threading.SynchronizationContext))
            {
                synchronizationContext = currentContext;
                return;
            }

            var currentScheduler = Threading.Tasks.Task.InternalCurrentScheduler;
            if (currentScheduler is not null && !currentScheduler.IsDefaultScheduler)
            {
                scheduler = currentScheduler;
            }
        }

        private void InvokeContinuation(
            Action<object?> continuation,
            object? state,
            Threading.SynchronizationContext? synchronizationContext,
            Threading.Tasks.TaskScheduler? scheduler,
            Threading.ExecutionContext? executionContext)
        {
            var invocation = new ContinuationInvocation(
                continuation,
                state,
                executionContext);
            if (synchronizationContext is not null)
            {
                synchronizationContext.Post(
                    static invocationState =>
                        ((ContinuationInvocation)invocationState!).Invoke(),
                    invocation);
                return;
            }
            if (scheduler is not null)
            {
                Threading.Tasks.Task.ScheduleAwaitContinuation(
                    scheduler,
                    invocation.Invoke);
                return;
            }
            if (RunContinuationsAsynchronously)
            {
                Runtime.InteropServices.PlatformServices.Scheduler.Schedule(
                    invocation.Invoke,
                    0);
                return;
            }
            invocation.Invoke();
        }

        private sealed class ContinuationInvocation
        {
            private readonly Action<object?> _continuation;
            private readonly object? _state;
            private readonly Threading.ExecutionContext? _executionContext;

            internal ContinuationInvocation(
                Action<object?> continuation,
                object? state,
                Threading.ExecutionContext? executionContext)
            {
                _continuation = continuation;
                _state = state;
                _executionContext = executionContext;
            }

            internal void Invoke()
            {
                if (_executionContext is null)
                {
                    _continuation(_state);
                    return;
                }

                Threading.ExecutionContext.Run(
                    _executionContext,
                    static state =>
                    {
                        var invocation = (ContinuationInvocation)state!;
                        invocation._continuation(invocation._state);
                    },
                    this);
            }
        }

        private void ValidateToken(short token)
        {
            if (token != _version)
            {
                throw new InvalidOperationException();
            }
        }
    }
}
