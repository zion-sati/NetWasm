// Portions derived from dotnet/runtime System.Private.CoreLib's
// ConfiguredCancelableAsyncEnumerable<T> at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements; the .NET
// Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    public readonly struct ConfiguredCancelableAsyncEnumerable<T>
        where T : allows ref struct
    {
        private readonly IAsyncEnumerable<T> _enumerable;
        private readonly CancellationToken _cancellationToken;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredCancelableAsyncEnumerable(
            IAsyncEnumerable<T> enumerable,
            bool continueOnCapturedContext,
            CancellationToken cancellationToken)
        {
            _enumerable = enumerable;
            _continueOnCapturedContext = continueOnCapturedContext;
            _cancellationToken = cancellationToken;
        }

        public ConfiguredCancelableAsyncEnumerable<T> ConfigureAwait(bool continueOnCapturedContext) =>
            new(_enumerable, continueOnCapturedContext, _cancellationToken);

        public ConfiguredCancelableAsyncEnumerable<T> WithCancellation(CancellationToken cancellationToken) =>
            new(_enumerable, _continueOnCapturedContext, cancellationToken);

        public Enumerator GetAsyncEnumerator() =>
            new(_enumerable.GetAsyncEnumerator(_cancellationToken), _continueOnCapturedContext);

        public readonly struct Enumerator
        {
            private readonly IAsyncEnumerator<T> _enumerator;
            private readonly bool _continueOnCapturedContext;

            internal Enumerator(IAsyncEnumerator<T> enumerator, bool continueOnCapturedContext)
            {
                _enumerator = enumerator;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public T Current { get => _enumerator.Current; }

            public ConfiguredValueTaskAwaitable<bool> MoveNextAsync() =>
                _enumerator.MoveNextAsync().ConfigureAwait(_continueOnCapturedContext);

            public ConfiguredValueTaskAwaitable DisposeAsync() =>
                _enumerator.DisposeAsync().ConfigureAwait(_continueOnCapturedContext);
        }
    }
}
