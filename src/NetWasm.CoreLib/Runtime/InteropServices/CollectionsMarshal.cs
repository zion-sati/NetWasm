// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib CollectionsMarshal.cs
// at commit 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    public static class CollectionsMarshal
    {
        public static Span<T> AsSpan<T>(List<T>? list)
        {
            Span<T> span = default;
            if (list is not null)
            {
                int size = list._size;
                T[] items = list._items;
                Debug.Assert(items is not null);

                if ((uint)size > (uint)items.Length)
                {
                    System.Collections.ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                }

                span = new Span<T>(ref MemoryMarshal.GetArrayDataReference(items), size);
            }

            return span;
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
            ref dictionary.FindValue(key);

        public static ref TValue GetValueRefOrNullRef<TKey, TValue, TAlternateKey>(
            Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey> dictionary,
            TAlternateKey key)
            where TKey : notnull
            where TAlternateKey : notnull, allows ref struct =>
            ref dictionary.FindValue(key, out _);

        public static ref TValue? GetValueRefOrAddDefault<TKey, TValue>(
            Dictionary<TKey, TValue> dictionary,
            TKey key,
            out bool exists)
            where TKey : notnull =>
            ref Dictionary<TKey, TValue>.CollectionsMarshalHelper.GetValueRefOrAddDefault(
                dictionary,
                key,
                out exists);

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
                System.Collections.ThrowHelper.ThrowArgumentOutOfRangeException_NeedNonNegNum(nameof(count));
            }

            list._version++;

            if (count > list.Capacity)
            {
                list.Grow(count);
            }
            else if (count < list._size && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(list._items, count, list._size - count);
            }

            list._size = count;
        }
    }
}
