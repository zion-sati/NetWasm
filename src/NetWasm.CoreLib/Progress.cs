// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;

namespace System
{
    public interface IProgress<in T>
    {
        void Report(T value);
    }

    public class Progress<T> : IProgress<T>
    {
        private readonly SynchronizationContext _synchronizationContext;
        private readonly Action<T>? _handler;
        private readonly SendOrPostCallback _invokeHandlers;

        public Progress()
        {
            _synchronizationContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _invokeHandlers = InvokeHandlers;
        }

        public Progress(Action<T> handler)
            : this()
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public event EventHandler<T>? ProgressChanged;

        protected virtual void OnReport(T value)
        {
            if (_handler is not null || ProgressChanged is not null)
            {
                _synchronizationContext.Post(_invokeHandlers, value);
            }
        }

        void IProgress<T>.Report(T value) => OnReport(value);

        private void InvokeHandlers(object? state)
        {
            var value = (T)state!;
            _handler?.Invoke(value);
            ProgressChanged?.Invoke(this, value);
        }
    }
}
