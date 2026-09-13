// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
//
// Dictionary-specific exceptions are kept here because the reduced CoreLib
// profile does not include the shared resource/ThrowHelper implementation.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections.Immutable
{
    internal static class ImmutableDictionaryThrowHelper
    {
        internal const string CollectionModifiedDuringEnumeration =
            "Collection was modified; enumeration operation may not execute.";

        internal static void ThrowKeyNotFoundException<TKey>(TKey key) =>
            throw new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");

        [DoesNotReturn]
        internal static void ThrowDuplicateKey<TKey>(TKey key) =>
            throw new ArgumentException("An element with the same key but a different value already exists. Key: '" + key + "'");
    }
}
