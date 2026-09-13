// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Collections.Immutable
// ImmutableCollectionsMarshal.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Immutable;

namespace System.Runtime.InteropServices
{
    /// <summary>
    /// Provides access to the backing storage of immutable collections.
    /// </summary>
    public static class ImmutableCollectionsMarshal
    {
        /// <summary>Wraps an array without making a defensive copy.</summary>
        public static ImmutableArray<T> AsImmutableArray<T>(T[]? array)
        {
            return new ImmutableArray<T>(array);
        }

        /// <summary>Gets the backing array, or <see langword="null"/> for default.</summary>
        public static T[]? AsArray<T>(ImmutableArray<T> array)
        {
            return array.array;
        }

        /// <summary>Gets the filled backing memory of a builder.</summary>
        public static Memory<T> AsMemory<T>(ImmutableArray<T>.Builder? builder)
        {
            return builder?.AsMemory() ?? default;
        }
    }
}
