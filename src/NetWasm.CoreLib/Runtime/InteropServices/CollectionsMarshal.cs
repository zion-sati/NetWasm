// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib CollectionsMarshal.cs
// at commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static class CollectionsMarshal
    {
        public static Span<T> AsSpan<T>(List<T>? list)
        {
            if (list == null)
            {
                return default;
            }

            // The span aliases the active portion of the list's backing array.
            // The caller must not add/remove items while the span is in use.
            return new Span<T>(list.Items, 0, list.Size);
        }

        public static Span<byte> AsBytes(System.Collections.BitArray? array)
        {
            if (array == null)
            {
                return default;
            }
            return new Span<byte>(
                array._array,
                0,
                System.Collections.BitArray.GetByteArrayLengthFromBitLength(array.Length));
        }

        public static ref TValue GetValueRefOrNullRef<TKey, TValue>(
            Dictionary<TKey, TValue> dictionary,
            TKey key)
            where TKey : notnull =>
            ref dictionary.GetValueRefOrNullRef(key);

        public static ref TValue GetValueRefOrNullRef<TKey, TValue, TAlternateKey>(
            Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey> dictionary,
            TAlternateKey key)
            where TKey : notnull
            where TAlternateKey : notnull, allows ref struct =>
            ref dictionary.GetValueRefOrNullRef(key);

        public static ref TValue? GetValueRefOrAddDefault<TKey, TValue>(
            Dictionary<TKey, TValue> dictionary,
            TKey key,
            out bool exists)
            where TKey : notnull =>
            ref dictionary.GetValueRefOrAddDefault(key, out exists);

        public static ref TValue? GetValueRefOrAddDefault<TKey, TValue, TAlternateKey>(
            Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey> dictionary,
            TAlternateKey key,
            out bool exists)
            where TKey : notnull
            where TAlternateKey : notnull, allows ref struct =>
            ref dictionary.GetValueRefOrAddDefault(key, out exists);

        public static void SetCount<T>(List<T> list, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            while (list.Count < count)
            {
                list.Add(default!);
            }
            while (list.Count > count)
            {
                list.RemoveAt(list.Count - 1);
            }
        }
    }
}
