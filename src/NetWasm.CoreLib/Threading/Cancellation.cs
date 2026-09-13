// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace System.Threading
{
    /// <summary>Propagates notification that operations should be canceled.</summary>
    public readonly struct CancellationToken : IEquatable<CancellationToken>
    {
        private readonly CancellationTokenSource? _source;

        internal CancellationToken(CancellationTokenSource? source) => _source = source;

        /// <summary>Returns a token that cannot be canceled.</summary>
        public static CancellationToken None => default;

        public CancellationToken(bool canceled) =>
            _source = canceled ? CancellationTokenSource.s_canceledSource : null;

        public bool IsCancellationRequested =>
            _source != null && _source.IsCancellationRequested;

        public bool CanBeCanceled => _source != null;

        public CancellationTokenRegistration Register(Action callback) =>
            Register(callback, useSynchronizationContext: false);

        public CancellationTokenRegistration Register(Action callback, bool useSynchronizationContext)
        {
            if (callback == null)
            {
                throw new ArgumentNullException();
            }

            return Register(
                static state => ((Action)state!).Invoke(),
                callback,
                useSynchronizationContext);
        }

        public CancellationTokenRegistration Register(Action<object?> callback, object? state) =>
            Register(callback, state, useSynchronizationContext: false);

        public CancellationTokenRegistration Register(
            Action<object?, CancellationToken> callback,
            object? state) =>
            Register(callback, state, useSynchronizationContext: false);

        public CancellationTokenRegistration Register(
            Action<object?> callback,
            object? state,
            bool useSynchronizationContext)
        {
            if (callback == null)
            {
                throw new ArgumentNullException();
            }

            return _source?.Register(
                callback,
                state,
                useSynchronizationContext ? SynchronizationContext.Current : null,
                ExecutionContext.Capture()) ?? default;
        }

        public CancellationTokenRegistration Register(
            Action<object?, CancellationToken> callback,
            object? state,
            bool useSynchronizationContext)
        {
            if (callback == null)
            {
                throw new ArgumentNullException();
            }

            return _source?.Register(
                callback,
                state,
                useSynchronizationContext ? SynchronizationContext.Current : null,
                ExecutionContext.Capture()) ?? default;
        }

        public CancellationTokenRegistration UnsafeRegister(Action<object?> callback, object? state) =>
            _source?.Register(
                callback ?? throw new ArgumentNullException(),
                state,
                synchronizationContext: null,
                executionContext: null) ?? default;

        public CancellationTokenRegistration UnsafeRegister(
            Action<object?, CancellationToken> callback,
            object? state) =>
            _source?.Register(
                callback ?? throw new ArgumentNullException(),
                state,
                synchronizationContext: null,
                executionContext: null) ?? default;

        public void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested)
            {
                throw new OperationCanceledException(this);
            }
        }

        public bool Equals(CancellationToken other) => _source == other._source;

        public override bool Equals(object? value) =>
            value is CancellationToken other && Equals(other);

        public override int GetHashCode() => _source?.GetHashCode() ?? 0;

        public static bool operator ==(CancellationToken left, CancellationToken right) =>
            left.Equals(right);

        public static bool operator !=(CancellationToken left, CancellationToken right) =>
            !left.Equals(right);
    }

    /// <summary>Represents a callback registered with a cancellation token.</summary>
    public readonly struct CancellationTokenRegistration :
        IAsyncDisposable, IDisposable, IEquatable<CancellationTokenRegistration>
    {
        private readonly CancellationTokenSource.CallbackNode? _node;

        internal CancellationTokenRegistration(CancellationTokenSource.CallbackNode node) =>
            _node = node;

        public void Dispose() => _node?.Unregister();

        public System.Threading.Tasks.ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public CancellationToken Token => _node is null
            ? default
            : new CancellationToken(_node.Source);

        public bool Unregister() => _node is not null && _node.Unregister();

        public bool Equals(CancellationTokenRegistration other) => _node == other._node;

        public override bool Equals(object? value) =>
            value is CancellationTokenRegistration other && Equals(other);

        public override int GetHashCode() => _node?.GetHashCode() ?? 0;

        public static bool operator ==(
            CancellationTokenRegistration left,
            CancellationTokenRegistration right) => left.Equals(right);

        public static bool operator !=(
            CancellationTokenRegistration left,
            CancellationTokenRegistration right) => !left.Equals(right);
    }

    /// <summary>Signals a cancellation token that it should be canceled.</summary>
    public class CancellationTokenSource : IDisposable
    {
        internal static readonly CancellationTokenSource s_canceledSource = CreateCanceledSource();
        private bool _cancelled;
        private bool _disposed;
        private CallbackNode? _callbacks;
        private IDisposable? _scheduledCancellation;
        private TimeProvider? _timeProvider;

        private static CancellationTokenSource CreateCanceledSource()
        {
            var source = new CancellationTokenSource();
            source._cancelled = true;
            return source;
        }

        public CancellationTokenSource()
        {
        }

        public CancellationTokenSource(TimeSpan delay) => ScheduleCancellation(delay);

        public CancellationTokenSource(TimeSpan delay, TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            ScheduleCancellation(delay);
        }

        public CancellationTokenSource(int millisecondsDelay) => ScheduleCancellation(millisecondsDelay);

        public bool IsCancellationRequested => _cancelled;

        public CancellationToken Token
        {
            get
            {
                ThrowIfDisposed();
                return new CancellationToken(this);
            }
        }

        public void Cancel() => Cancel(throwOnFirstException: false);

        public void Cancel(bool throwOnFirstException)
        {
            ThrowIfDisposed();
            if (!TransitionToCancellationRequested())
            {
                return;
            }

            ExecuteCallbacks(throwOnFirstException);
        }

        private bool TransitionToCancellationRequested()
        {
            if (_cancelled)
            {
                return false;
            }

            _cancelled = true;
            _scheduledCancellation?.Dispose();
            _scheduledCancellation = null;
            return true;
        }

        private void ExecuteCallbacks(bool throwOnFirstException)
        {
            List<Exception>? exceptions = null;
            while (_callbacks is not null)
            {
                var callback = _callbacks;
                _callbacks = callback.Next;
                callback.Next = null;
                callback.BeginInvoke();

                try
                {
                    callback.Invoke();
                }
                catch (Exception exception)
                {
                    if (throwOnFirstException)
                    {
                        DiscardCallbacks();
                        throw;
                    }

                    (exceptions ??= new List<Exception>()).Add(exception);
                }
                finally
                {
                    callback.Complete();
                }
            }

            if (exceptions is not null)
            {
                throw new AggregateException(exceptions);
            }
        }

        private void DiscardCallbacks()
        {
            while (_callbacks is not null)
            {
                var callback = _callbacks;
                _callbacks = callback.Next;
                callback.Next = null;
                callback.Complete();
            }
        }

        public System.Threading.Tasks.Task CancelAsync()
        {
            if (_disposed)
            {
                return System.Threading.Tasks.Task.FromException(
                    new ObjectDisposedException(nameof(CancellationTokenSource)));
            }

            if (!TransitionToCancellationRequested())
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            if (_callbacks is null)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            var task = new System.Threading.Tasks.Task();
            try
            {
                PlatformServices.Scheduler.Schedule(() =>
                {
                    try
                    {
                        ExecuteCallbacks(throwOnFirstException: false);
                        task.SetResult();
                    }
                    catch (Exception exception)
                    {
                        task.SetException(exception);
                    }
                }, 0);
            }
            catch (Exception exception)
            {
                task.SetException(exception);
            }

            return task;
        }

        public void CancelAfter(TimeSpan delay) => ScheduleCancellation(delay);

        public void CancelAfter(int millisecondsDelay) => ScheduleCancellation(millisecondsDelay);

        public bool TryReset()
        {
            ThrowIfDisposed();
            // A token must never transition from canceled back to non-canceled.
            // Reset is only valid before cancellation has been requested; it
            // clears registrations so the source can be reused by its owner.
            if (_cancelled)
            {
                return false;
            }

            _scheduledCancellation?.Dispose();
            _scheduledCancellation = null;
            while (_callbacks is not null)
            {
                var callback = _callbacks;
                _callbacks = callback.Next;
                callback.Next = null;
                callback.Unregister();
            }

            return true;
        }

        public void Dispose() => Dispose(disposing: true);

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || _disposed)
            {
                return;
            }

            _disposed = true;
            _scheduledCancellation?.Dispose();
            _scheduledCancellation = null;

            while (_callbacks is not null)
            {
                var callback = _callbacks;
                _callbacks = callback.Next;
                callback.Next = null;
                callback.Unregister();
            }
        }

        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token) =>
            token.CanBeCanceled
                ? new LinkedCancellationTokenSource([token])
                : new CancellationTokenSource();

        public static CancellationTokenSource CreateLinkedTokenSource(
            CancellationToken token1,
            CancellationToken token2) =>
            token1.CanBeCanceled || token2.CanBeCanceled
                ? new LinkedCancellationTokenSource([token1, token2])
                : new CancellationTokenSource();

        public static CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens)
        {
            if (tokens == null)
            {
                throw new ArgumentNullException();
            }

            return CreateLinkedTokenSource((ReadOnlySpan<CancellationToken>)tokens);
        }

        public static CancellationTokenSource CreateLinkedTokenSource(
            params ReadOnlySpan<CancellationToken> tokens)
        {
            if (tokens.Length == 0)
            {
                throw new ArgumentException();
            }

            return tokens.Length switch
            {
                1 => CreateLinkedTokenSource(tokens[0]),
                2 => CreateLinkedTokenSource(tokens[0], tokens[1]),
                _ => new LinkedCancellationTokenSource(tokens),
            };
        }

        internal CancellationTokenRegistration Register(
            Action<object?> callback,
            object? state,
            SynchronizationContext? synchronizationContext,
            ExecutionContext? executionContext)
        {
            if (_disposed || _cancelled)
            {
                if (_cancelled)
                {
                    Invoke(
                        callback,
                        state,
                        this,
                        synchronizationContext,
                        executionContext);
                }

                return default;
            }

            var node = new CallbackNode(
                this,
                callback,
                state,
                synchronizationContext,
                executionContext);
            node.Next = _callbacks;
            _callbacks = node;
            return new CancellationTokenRegistration(node);
        }

        internal CancellationTokenRegistration Register(
            Action<object?, CancellationToken> callback,
            object? state,
            SynchronizationContext? synchronizationContext,
            ExecutionContext? executionContext)
        {
            if (_disposed || _cancelled)
            {
                if (_cancelled)
                {
                    Invoke(
                        callback,
                        state,
                        this,
                        synchronizationContext,
                        executionContext);
                }

                return default;
            }

            var node = new CallbackNode(
                this,
                callback,
                state,
                synchronizationContext,
                executionContext);
            node.Next = _callbacks;
            _callbacks = node;
            return new CancellationTokenRegistration(node);
        }

        private void ScheduleCancellation(TimeSpan delay)
        {
            var milliseconds = (long)delay.TotalMilliseconds;
            if (milliseconds < -1 || milliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            ScheduleCancellation((int)milliseconds);
        }

        private void ScheduleCancellation(int millisecondsDelay)
        {
            ThrowIfDisposed();
            if (millisecondsDelay < -1)
            {
                throw new ArgumentOutOfRangeException();
            }

            _scheduledCancellation?.Dispose();
            _scheduledCancellation = null;
            if (_cancelled || millisecondsDelay == -1)
            {
                return;
            }

            _scheduledCancellation = _timeProvider is null ||
                ReferenceEquals(_timeProvider, TimeProvider.System)
                ? PlatformServices.Scheduler.Schedule(
                    CancelFromSchedule,
                    checked((ulong)millisecondsDelay * 1_000_000UL))
                : _timeProvider.CreateTimer(
                    _ => CancelFromSchedule(),
                    null,
                    TimeSpan.FromMilliseconds(millisecondsDelay),
                    TimeSpan.FromMilliseconds(-1));
        }

        private void CancelFromSchedule()
        {
            _scheduledCancellation = null;
            Cancel();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CancellationTokenSource));
            }
        }

        private void Remove(CallbackNode node)
        {
            CallbackNode? previous = null;
            var current = _callbacks;
            while (current is not null)
            {
                if (ReferenceEquals(current, node))
                {
                    if (previous is null)
                    {
                        _callbacks = current.Next;
                    }
                    else
                    {
                        previous.Next = current.Next;
                    }

                    current.Next = null;
                    return;
                }

                previous = current;
                current = current.Next;
            }
        }

        private static void Invoke(
            Action<object?> callback,
            object? state,
            CancellationTokenSource source,
            SynchronizationContext? synchronizationContext,
            ExecutionContext? executionContext)
        {
            if (executionContext is not null)
            {
                ExecutionContext.Run(
                    executionContext,
                    static invocationState =>
                    {
                        var invocation =
                            ((Action<object?>, object?, CancellationTokenSource, SynchronizationContext?))
                            invocationState!;
                        Invoke(
                            invocation.Item1,
                            invocation.Item2,
                            invocation.Item3,
                            invocation.Item4,
                            executionContext: null);
                    },
                    (callback, state, source, synchronizationContext));
                return;
            }

            if (synchronizationContext is null)
            {
                callback(state);
                return;
            }

            synchronizationContext.Send(
                static value =>
                {
                    var invocation = ((Action<object?>, object?))value!;
                    invocation.Item1(invocation.Item2);
                },
                (callback, state));
        }

        private static void Invoke(
            Action<object?, CancellationToken> callback,
            object? state,
            CancellationTokenSource source,
            SynchronizationContext? synchronizationContext,
            ExecutionContext? executionContext)
        {
            if (executionContext is not null)
            {
                ExecutionContext.Run(
                    executionContext,
                    static invocationState =>
                    {
                        var invocation =
                            ((Action<object?, CancellationToken>, object?, CancellationTokenSource, SynchronizationContext?))
                            invocationState!;
                        Invoke(
                            invocation.Item1,
                            invocation.Item2,
                            invocation.Item3,
                            invocation.Item4,
                            executionContext: null);
                    },
                    (callback, state, source, synchronizationContext));
                return;
            }

            if (synchronizationContext is null)
            {
                callback(state, new CancellationToken(source));
                return;
            }

            synchronizationContext.Send(
                static value =>
                {
                    var invocation = ((Action<object?, CancellationToken>, object?, CancellationToken))value!;
                    invocation.Item1(invocation.Item2, invocation.Item3);
                },
                (callback, state, new CancellationToken(source)));
        }

        private sealed class LinkedCancellationTokenSource : CancellationTokenSource
        {
            private CancellationTokenRegistration[]? _registrations;

            internal LinkedCancellationTokenSource(ReadOnlySpan<CancellationToken> tokens)
            {
                _registrations = new CancellationTokenRegistration[tokens.Length];
                for (var index = 0; index < tokens.Length; index++)
                {
                    if (tokens[index].CanBeCanceled)
                    {
                        _registrations[index] = tokens[index].UnsafeRegister(
                            static (state, _) => ((CancellationTokenSource)state!).Cancel(),
                            this);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _registrations is not null)
                {
                    var registrations = _registrations;
                    _registrations = null;
                    foreach (var registration in registrations)
                    {
                        registration.Dispose();
                    }
                }

                base.Dispose(disposing);
            }
        }

        internal sealed class CallbackNode
        {
            private readonly Action<object?>? _callback;
            private readonly Action<object?, CancellationToken>? _callbackWithToken;
            private readonly object? _state;
            private readonly SynchronizationContext? _synchronizationContext;
            private readonly ExecutionContext? _executionContext;
            private bool _registered = true;
            private bool _executing;

            internal CallbackNode(
                CancellationTokenSource source,
                Action<object?> callback,
                object? state,
                SynchronizationContext? synchronizationContext,
                ExecutionContext? executionContext)
            {
                Source = source;
                _callback = callback;
                _state = state;
                _synchronizationContext = synchronizationContext;
                _executionContext = executionContext;
            }

            internal CallbackNode(
                CancellationTokenSource source,
                Action<object?, CancellationToken> callback,
                object? state,
                SynchronizationContext? synchronizationContext,
                ExecutionContext? executionContext)
            {
                Source = source;
                _callbackWithToken = callback;
                _state = state;
                _synchronizationContext = synchronizationContext;
                _executionContext = executionContext;
            }

            internal CancellationTokenSource Source { get; }
            internal CallbackNode? Next { get; set; }

            internal bool Unregister()
            {
                if (!_registered)
                {
                    return false;
                }

                _registered = false;
                if (!_executing)
                {
                    Source.Remove(this);
                }

                return true;
            }

            internal void BeginInvoke() => _executing = true;

            internal void Invoke()
            {
                if (_callback is not null)
                {
                    CancellationTokenSource.Invoke(
                        _callback,
                        _state,
                        Source,
                        _synchronizationContext,
                        _executionContext);
                }
                else
                {
                    CancellationTokenSource.Invoke(
                        _callbackWithToken!,
                        _state,
                        Source,
                        _synchronizationContext,
                        _executionContext);
                }
            }

            internal void Complete()
            {
                _executing = false;
                _registered = false;
            }
        }
    }
}
