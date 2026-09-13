// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Runtime.ExceptionServices;

namespace System.Threading
{
    /// <summary>Captures ambient values for one managed reactor execution flow.</summary>
    public sealed class ExecutionContext : IDisposable
    {
        private static readonly ExecutionContext s_default =
            new(null, flowSuppressed: false);

        // NetWasm has one managed reactor. This is the reactor's ambient context,
        // rather than a claim of thread-local or cross-worker execution semantics.
        private static ExecutionContext s_current = s_default;
        private static int s_nextFlowControlToken;
        private static int s_currentFlowControlToken;

        private readonly LocalValue? _localValues;
        private readonly bool _flowSuppressed;
        private ExecutionContext(
            LocalValue? localValues,
            bool flowSuppressed)
        {
            _localValues = localValues;
            _flowSuppressed = flowSuppressed;
        }

        /// <summary>Captures the current flow, or null while flow is suppressed.</summary>
        public static ExecutionContext? Capture() =>
            s_current._flowSuppressed ? null : s_current;

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

        /// <summary>Creates an immutable copy of this context.</summary>
        public ExecutionContext CreateCopy() => this;

        /// <summary>ExecutionContext is immutable; disposal has no state to release.</summary>
        public void Dispose()
        {
        }

        internal static object? GetLocalValue(IAsyncLocal local)
        {
            for (var current = s_current._localValues; current is not null; current = current.Next)
            {
                if (ReferenceEquals(current.Local, local))
                {
                    return current.Value;
                }
            }

            return null;
        }

        internal static void SetLocalValue(
            IAsyncLocal local,
            object? newValue,
            bool needChangeNotifications)
        {
            var previousValue = FindValue(s_current._localValues, local, out var hadPreviousValue);
            if (ReferenceEquals(previousValue, newValue))
            {
                return;
            }

            var values = s_current._localValues;
            if (hadPreviousValue)
            {
                values = newValue is null && !needChangeNotifications
                    ? RemoveValue(values, local)
                    : ReplaceValue(values!, local, newValue);
            }
            else if (newValue is not null || needChangeNotifications)
            {
                values = new LocalValue(local, newValue, values);
            }

            s_current = CreateContext(
                values,
                s_current._flowSuppressed);

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
            CreateContext(_localValues, flowSuppressed);

        private static ExecutionContext CreateContext(
            LocalValue? values,
            bool flowSuppressed)
        {
            if (values is null)
            {
                return flowSuppressed
                    ? new ExecutionContext(null, flowSuppressed: true)
                    : s_default;
            }

            return new ExecutionContext(
                values,
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

        private static object? FindValue(
            LocalValue? values,
            IAsyncLocal local,
            out bool found)
        {
            for (var current = values; current is not null; current = current.Next)
            {
                if (ReferenceEquals(current.Local, local))
                {
                    found = true;
                    return current.Value;
                }
            }

            found = false;
            return null;
        }

        private static LocalValue? ReplaceValue(
            LocalValue values,
            IAsyncLocal local,
            object? value)
        {
            if (ReferenceEquals(values.Local, local))
            {
                return new LocalValue(local, value, values.Next);
            }

            return new LocalValue(
                values.Local,
                values.Value,
                values.Next is null ? null : ReplaceValue(values.Next, local, value));
        }

        private static LocalValue? RemoveValue(LocalValue? values, IAsyncLocal local)
        {
            if (values is null)
            {
                return null;
            }

            if (ReferenceEquals(values.Local, local))
            {
                return values.Next;
            }

            return new LocalValue(
                values.Local,
                values.Value,
                RemoveValue(values.Next, local));
        }

        private static void NotifyValuesChanged(
            ExecutionContext previous,
            ExecutionContext next)
        {
            for (var current = previous._localValues; current is not null; current = current.Next)
            {
                if (!current.Local.HasValueChangedHandler)
                {
                    continue;
                }

                var nextValue = FindValue(next._localValues, current.Local, out _);
                if (!ReferenceEquals(current.Value, nextValue))
                {
                    current.Local.OnValueChanged(
                        current.Value,
                        nextValue,
                        contextChanged: true);
                }
            }

            for (var current = next._localValues; current is not null; current = current.Next)
            {
                if (!current.Local.HasValueChangedHandler)
                {
                    continue;
                }

                FindValue(previous._localValues, current.Local, out var hadPreviousValue);
                if (hadPreviousValue)
                {
                    continue;
                }

                if (current.Value is not null)
                {
                    current.Local.OnValueChanged(
                        previousValue: null,
                        current.Value,
                        contextChanged: true);
                }
            }
        }

        private sealed class LocalValue
        {
            internal LocalValue(IAsyncLocal local, object? value, LocalValue? next)
            {
                Local = local;
                Value = value;
                Next = next;
            }

            internal IAsyncLocal Local { get; }
            internal object? Value { get; }
            internal LocalValue? Next { get; }
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
