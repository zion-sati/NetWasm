// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Minimal immutable-collection exception helper adapted from the pinned
// System.Collections.Immutable implementation at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Immutable
{
    internal static class ThrowHelper
    {
        internal static void ThrowInvalidOperationException() =>
            throw new InvalidOperationException();
    }
}
