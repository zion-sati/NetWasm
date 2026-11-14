// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct ConfiguredValueTaskAwaitable
    {
        private readonly global::System.Runtime.CompilerServices.ValueTaskAwaiter _awaiter;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredValueTaskAwaitable(
            global::System.Runtime.CompilerServices.ValueTaskAwaiter awaiter,
            bool continueOnCapturedContext)
        {
            _awaiter = awaiter;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        public ConfiguredValueTaskAwaiter GetAwaiter() =>
            new(_awaiter, _continueOnCapturedContext);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
        {
            private readonly global::System.Runtime.CompilerServices.ValueTaskAwaiter _awaiter;
            private readonly bool _continueOnCapturedContext;

            internal ConfiguredValueTaskAwaiter(
                global::System.Runtime.CompilerServices.ValueTaskAwaiter awaiter,
                bool continueOnCapturedContext)
            {
                _awaiter = awaiter;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public bool IsCompleted => _awaiter.IsCompleted;
            public void OnCompleted(Action continuation) =>
                _awaiter.OnCompleted(continuation, _continueOnCapturedContext);
            public void UnsafeOnCompleted(Action continuation) =>
                _awaiter.UnsafeOnCompleted(continuation, _continueOnCapturedContext);
            public void GetResult() => _awaiter.GetResult();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct ConfiguredValueTaskAwaitable<T>
    {
        private readonly global::System.Runtime.CompilerServices.ValueTaskAwaiter<T> _awaiter;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredValueTaskAwaitable(
            global::System.Runtime.CompilerServices.ValueTaskAwaiter<T> awaiter,
            bool continueOnCapturedContext)
        {
            _awaiter = awaiter;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        public ConfiguredValueTaskAwaiter GetAwaiter() =>
            new(_awaiter, _continueOnCapturedContext);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
        public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
        {
            private readonly global::System.Runtime.CompilerServices.ValueTaskAwaiter<T> _awaiter;
            private readonly bool _continueOnCapturedContext;

            internal ConfiguredValueTaskAwaiter(
                global::System.Runtime.CompilerServices.ValueTaskAwaiter<T> awaiter,
                bool continueOnCapturedContext)
            {
                _awaiter = awaiter;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public bool IsCompleted => _awaiter.IsCompleted;
            public void OnCompleted(Action continuation) =>
                _awaiter.OnCompleted(continuation, _continueOnCapturedContext);
            public void UnsafeOnCompleted(Action continuation) =>
                _awaiter.UnsafeOnCompleted(continuation, _continueOnCapturedContext);
            public T GetResult() => _awaiter.GetResult();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly struct ConfiguredAsyncDisposable
    {
        private readonly IAsyncDisposable _source;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredAsyncDisposable(IAsyncDisposable source, bool continueOnCapturedContext)
        {
            _source = source;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        public ConfiguredValueTaskAwaitable DisposeAsync() =>
            _source.DisposeAsync().ConfigureAwait(_continueOnCapturedContext);
    }
}
