// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Portions adapted from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Threading
{
    /// <summary>Represents data local to one asynchronous control flow.</summary>
    public sealed class AsyncLocal<T> : IAsyncLocal
    {
        private readonly Action<AsyncLocalValueChangedArgs<T>>? _valueChangedHandler;

        /// <summary>Creates an ambient value without change notifications.</summary>
        public AsyncLocal()
        {
        }

        /// <summary>Creates an ambient value with a change notification callback.</summary>
        public AsyncLocal(Action<AsyncLocalValueChangedArgs<T>>? valueChangedHandler)
        {
            _valueChangedHandler = valueChangedHandler;
        }

        /// <summary>Gets or sets the value for the current execution context.</summary>
        public T? Value
        {
            get
            {
                var value = ExecutionContext.GetLocalValue(this);
                return value is null ? default! : (T)value;
            }
            set => ExecutionContext.SetLocalValue(
                this,
                value,
                _valueChangedHandler is not null);
        }

        bool IAsyncLocal.HasValueChangedHandler => _valueChangedHandler is not null;

        void IAsyncLocal.OnValueChanged(
            object? previousValue,
            object? currentValue,
            bool contextChanged)
        {
            var handler = _valueChangedHandler;
            if (handler is null)
            {
                return;
            }

            var previous = previousValue is null ? default! : (T)previousValue;
            var current = currentValue is null ? default! : (T)currentValue;
            handler(new AsyncLocalValueChangedArgs<T>(previous, current, contextChanged));
        }
    }

    /// <summary>Internal non-generic callback contract used by ExecutionContext.</summary>
    internal interface IAsyncLocal
    {
        bool HasValueChangedHandler { get; }

        void OnValueChanged(object? previousValue, object? currentValue, bool contextChanged);
    }
}
