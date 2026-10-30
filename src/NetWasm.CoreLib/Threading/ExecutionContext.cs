// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Runtime.ExceptionServices;

namespace System.Threading
{
    /// <summary>Captures ambient values for one managed reactor execution flow.</summary>
    public sealed class ExecutionContext : IDisposable
    {
        private static readonly ExecutionContext s_default =
            new(
                AsyncLocalValueMap.Empty,
                localChangeNotifications: null,
                flowSuppressed: false);
        private static readonly ExecutionContext s_defaultFlowSuppressed =
            new(
                AsyncLocalValueMap.Empty,
                localChangeNotifications: null,
                flowSuppressed: true);

        // NetWasm has one managed reactor. This is the reactor's ambient context,
        // rather than a claim of thread-local or cross-worker execution semantics.
        private static ExecutionContext s_current = s_default;
        private static int s_nextFlowControlToken;
        private static int s_currentFlowControlToken;

        private readonly IAsyncLocalValueMap _localValues;
        private readonly IAsyncLocal[]? _localChangeNotifications;
        private readonly bool _flowSuppressed;
        private ExecutionContext(
            IAsyncLocalValueMap localValues,
            IAsyncLocal[]? localChangeNotifications,
            bool flowSuppressed)
        {
            _localValues = localValues;
            _localChangeNotifications = localChangeNotifications;
            _flowSuppressed = flowSuppressed;
        }

        /// <summary>Captures the current flow, or null while flow is suppressed.</summary>
        public static ExecutionContext? Capture() =>
            s_current._flowSuppressed ? null : s_current;

        internal static ExecutionContext CaptureForRestore(
            out int flowControlToken)
        {
            flowControlToken = s_currentFlowControlToken;
            return s_current;
        }

        /// <summary>Suppresses propagation of the current execution context.</summary>
        public static AsyncFlowControl SuppressFlow()
        {
            if (s_current._flowSuppressed)
            {
                return default;
            }

            var token = NextFlowControlToken();
            s_current = s_current.WithFlowSuppressed(true);
            s_currentFlowControlToken = token;
            return AsyncFlowControl.Create(token);
        }

        /// <summary>Restores propagation after a matching flow suppression.</summary>
        public static void RestoreFlow()
        {
            if (!s_current._flowSuppressed)
            {
                throw new InvalidOperationException(
                    "ExecutionContext flow is not suppressed.");
            }

            s_current = s_current.WithFlowSuppressed(false);
            s_currentFlowControlToken = 0;
        }

        /// <summary>Gets whether propagation is currently suppressed.</summary>
        public static bool IsFlowSuppressed() => s_current._flowSuppressed;

        internal static bool IsFlowSuppressedBy(int token) =>
            token != 0 &&
            s_current._flowSuppressed &&
            s_currentFlowControlToken == token;

        /// <summary>Runs a callback under a captured context and restores the caller's context.</summary>
        public static void Run(
            ExecutionContext executionContext,
            ContextCallback callback,
            object? state)
        {
            if (executionContext is null)
            {
                throw new ArgumentNullException(nameof(executionContext));
            }

            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var previous = s_current;
            var previousSynchronizationContext = SynchronizationContext.Current;
            var previousFlowControlToken = s_currentFlowControlToken;
            Exception? failure = null;
            try
            {
                SetCurrent(executionContext, contextChanged: true);
                callback(state);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    previousSynchronizationContext);
                SetCurrent(previous, contextChanged: true);
                s_currentFlowControlToken = previousFlowControlToken;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>Restores a captured context until another context is applied.</summary>
        public static void Restore(ExecutionContext executionContext)
        {
            if (executionContext is null)
            {
                throw new ArgumentNullException(nameof(executionContext));
            }

            SetCurrent(executionContext, contextChanged: true);
        }

        internal static void Restore(
            ExecutionContext executionContext,
            int flowControlToken)
        {
            Restore(executionContext);
            s_currentFlowControlToken = flowControlToken;
        }

        /// <summary>Creates an immutable copy of this context.</summary>
        public ExecutionContext CreateCopy() => this;

        /// <summary>ExecutionContext is immutable; disposal has no state to release.</summary>
        public void Dispose()
        {
        }

        internal static object? GetLocalValue(IAsyncLocal local)
        {
            s_current._localValues.TryGetValue(local, out var value);
            return value;
        }

        internal static void SetLocalValue(
            IAsyncLocal local,
            object? newValue,
            bool needChangeNotifications)
        {
            var current = s_current;
            var hadPreviousValue = current._localValues.TryGetValue(
                local,
                out var previousValue);
            if (ReferenceEquals(previousValue, newValue))
            {
                return;
            }

            var values = current._localValues.Set(
                local,
                newValue,
                treatNullValueAsNonexistent: !needChangeNotifications);
            var changeNotifications = current._localChangeNotifications;
            if (needChangeNotifications && !hadPreviousValue)
            {
                if (changeNotifications is null)
                {
                    changeNotifications = [local];
                }
                else
                {
                    var notificationIndex = changeNotifications.Length;
                    Array.Resize(
                        ref changeNotifications,
                        notificationIndex + 1);
                    changeNotifications[notificationIndex] = local;
                }
            }

            s_current = CreateContext(
                values,
                changeNotifications,
                current._flowSuppressed);

            if (needChangeNotifications)
            {
                local.OnValueChanged(previousValue, newValue, contextChanged: false);
            }
        }

        private static void SetCurrent(ExecutionContext next, bool contextChanged)
        {
            var previous = s_current;
            if (ReferenceEquals(previous, next))
            {
                return;
            }

            s_current = next;
            if (contextChanged)
            {
                NotifyValuesChanged(previous, next);
            }
        }

        private ExecutionContext WithFlowSuppressed(bool flowSuppressed) =>
            CreateContext(
                _localValues,
                _localChangeNotifications,
                flowSuppressed);

        private static ExecutionContext CreateContext(
            IAsyncLocalValueMap values,
            IAsyncLocal[]? localChangeNotifications,
            bool flowSuppressed)
        {
            if (AsyncLocalValueMap.IsEmpty(values))
            {
                return flowSuppressed
                    ? s_defaultFlowSuppressed
                    : s_default;
            }

            return new ExecutionContext(
                values,
                localChangeNotifications,
                flowSuppressed);
        }

        private static int NextFlowControlToken()
        {
            do
            {
                s_nextFlowControlToken++;
            }
            while (s_nextFlowControlToken == 0);

            return s_nextFlowControlToken;
        }

        private static void NotifyValuesChanged(
            ExecutionContext previous,
            ExecutionContext next)
        {
            var previousNotifications = previous._localChangeNotifications;
            var nextNotifications = next._localChangeNotifications;

            if (previousNotifications is not null &&
                nextNotifications is not null)
            {
                foreach (var local in previousNotifications)
                {
                    previous._localValues.TryGetValue(
                        local,
                        out var previousValue);
                    next._localValues.TryGetValue(local, out var currentValue);
                    if (!ReferenceEquals(previousValue, currentValue))
                    {
                        local.OnValueChanged(
                            previousValue,
                            currentValue,
                            contextChanged: true);
                    }
                }

                if (!ReferenceEquals(
                    previousNotifications,
                    nextNotifications))
                {
                    foreach (var local in nextNotifications)
                    {
                        if (previous._localValues.TryGetValue(
                            local,
                            out var previousValue))
                        {
                            continue;
                        }

                        next._localValues.TryGetValue(
                            local,
                            out var currentValue);
                        if (!ReferenceEquals(previousValue, currentValue))
                        {
                            local.OnValueChanged(
                                previousValue,
                                currentValue,
                                contextChanged: true);
                        }
                    }
                }

                return;
            }

            if (previousNotifications is not null)
            {
                foreach (var local in previousNotifications)
                {
                    previous._localValues.TryGetValue(
                        local,
                        out var previousValue);
                    if (previousValue is not null)
                    {
                        local.OnValueChanged(
                            previousValue,
                            currentValue: null,
                            contextChanged: true);
                    }
                }

                return;
            }

            if (nextNotifications is not null)
            {
                foreach (var local in nextNotifications)
                {
                    next._localValues.TryGetValue(local, out var currentValue);
                    if (currentValue is not null)
                    {
                        local.OnValueChanged(
                            previousValue: null,
                            currentValue,
                            contextChanged: true);
                    }
                }
            }
        }
    }

    /// <summary>Controls one matching suppression of execution-context flow.</summary>
    public struct AsyncFlowControl : IEquatable<AsyncFlowControl>, IDisposable
    {
        private int _token;

        internal static AsyncFlowControl Create(int token) => new() { _token = token };

        public void Undo()
        {
            if (_token == 0)
            {
                return;
            }

            if (!ExecutionContext.IsFlowSuppressedBy(_token))
            {
                throw new InvalidOperationException(
                    "ExecutionContext flow is not suppressed.");
            }

            _token = 0;
            ExecutionContext.RestoreFlow();
        }

        public void Dispose() => Undo();

        public bool Equals(AsyncFlowControl other) => _token == other._token;

        public override bool Equals(object? obj) =>
            obj is AsyncFlowControl other && Equals(other);

        public override int GetHashCode() => _token;

        public static bool operator ==(AsyncFlowControl left, AsyncFlowControl right) =>
            left.Equals(right);

        public static bool operator !=(AsyncFlowControl left, AsyncFlowControl right) =>
            !left.Equals(right);
    }
}
