// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections
{
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowIfDestinationTooSmall() =>
            throw new ArgumentException(SR.CapacityMustBeGreaterThanOrEqualToCount, "destination");

        [DoesNotReturn]
        internal static void ThrowArgumentNullException(string? paramName) =>
            throw new ArgumentNullException(paramName);

        [DoesNotReturn]
        internal static void ThrowKeyNotFoundException() =>
            throw new KeyNotFoundException();

        [DoesNotReturn]
        internal static void ThrowKeyNotFoundException<TKey>(TKey key) =>
            throw new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, key));

        [DoesNotReturn]
        internal static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException();

        [DoesNotReturn]
        internal static void ThrowIncompatibleComparer() =>
            throw new InvalidOperationException(SR.InvalidOperation_IncompatibleComparer);
    }
}
