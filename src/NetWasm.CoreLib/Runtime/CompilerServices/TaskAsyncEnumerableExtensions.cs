// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Threading.Tasks
{
    public static partial class TaskAsyncEnumerableExtensions
    {
        public static ConfiguredAsyncDisposable ConfigureAwait(
            this IAsyncDisposable source,
            bool continueOnCapturedContext) =>
            new(source ?? throw new ArgumentNullException(nameof(source)), continueOnCapturedContext);

        public static ConfiguredCancelableAsyncEnumerable<T> ConfigureAwait<T>(
            this IAsyncEnumerable<T> source,
            bool continueOnCapturedContext)
            where T : allows ref struct =>
            new(source ?? throw new ArgumentNullException(nameof(source)), continueOnCapturedContext, default);

        public static ConfiguredCancelableAsyncEnumerable<T> WithCancellation<T>(
            this IAsyncEnumerable<T> source,
            CancellationToken cancellationToken)
            where T : allows ref struct =>
            new(source ?? throw new ArgumentNullException(nameof(source)), true, cancellationToken);
    }
}
