// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib CollectionExtensions.cs
// at commit 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    public static class CollectionExtensions
    {
        public static TValue? GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dictionary,
            TKey key) => dictionary.GetValueOrDefault(key, default!);

        public static TValue GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue)
        {
            if (dictionary == null)
            {
                throw new System.ArgumentNullException();
            }
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }

        public static bool TryAdd<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue value)
        {
            if (dictionary == null)
            {
                throw new System.ArgumentNullException();
            }
            if (dictionary.ContainsKey(key))
            {
                return false;
            }
            dictionary.Add(key, value);
            return true;
        }

        public static bool Remove<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary,
            TKey key,
            out TValue value)
        {
            if (dictionary == null)
            {
                throw new System.ArgumentNullException();
            }
            if (dictionary.TryGetValue(key, out value))
            {
                dictionary.Remove(key);
                return true;
            }
            value = default!;
            return false;
        }

        public static System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly<T>(
            this IList<T> list) => new(list ?? throw new System.ArgumentNullException());

        public static System.Collections.ObjectModel.ReadOnlySet<T> AsReadOnly<T>(
            this ISet<T> set) => new(set ?? throw new System.ArgumentNullException());

        public static System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue> AsReadOnly<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary)
            where TKey : notnull =>
            new(dictionary ?? throw new System.ArgumentNullException());

        public static void AddRange<T>(this List<T> list, params ReadOnlySpan<T> source)
        {
            if (list == null)
            {
                throw new System.ArgumentNullException();
            }
            for (var index = 0; index < source.Length; index++)
            {
                list.Add(source[index]);
            }
        }

        public static void InsertRange<T>(
            this List<T> list,
            int index,
            params ReadOnlySpan<T> source)
        {
            if (list == null)
            {
                throw new System.ArgumentNullException();
            }
            if (index < 0 || index > list.Count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            for (var offset = 0; offset < source.Length; offset++)
            {
                list.Insert(index + offset, source[offset]);
            }
        }

        public static void CopyTo<T>(this List<T> list, Span<T> destination)
        {
            if (list == null)
            {
                throw new System.ArgumentNullException();
            }
            if (destination.Length < list.Count)
            {
                throw new System.ArgumentException();
            }
            for (var index = 0; index < list.Count; index++)
            {
                destination[index] = list[index];
            }
        }
    }
}
