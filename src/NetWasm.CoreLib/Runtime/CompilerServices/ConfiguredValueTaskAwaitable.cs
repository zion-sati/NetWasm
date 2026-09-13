// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
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

namespace System.Threading.Tasks
{
    public static class ValueTaskExtensions
    {
        public static Runtime.CompilerServices.ConfiguredValueTaskAwaitable ConfigureAwait(
            this ValueTask value,
            bool continueOnCapturedContext) =>
            new(value.GetAwaiter(), continueOnCapturedContext);

        public static Runtime.CompilerServices.ConfiguredValueTaskAwaitable<TResult> ConfigureAwait<TResult>(
            this ValueTask<TResult> value,
            bool continueOnCapturedContext) =>
            new(value.GetAwaiter(), continueOnCapturedContext);

        public static Runtime.CompilerServices.ConfiguredAsyncDisposable ConfigureAwait(
            this IAsyncDisposable source,
            bool continueOnCapturedContext) =>
            new(source ?? throw new ArgumentNullException(nameof(source)), continueOnCapturedContext);

    }
}
