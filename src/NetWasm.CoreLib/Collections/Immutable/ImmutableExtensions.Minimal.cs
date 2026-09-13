// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Collections.Immutable
// ImmutableExtensions.Minimal.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. LINQ's ToList fallback is kept
// as an explicit enumeration so this CoreLib helper has no LINQ dependency.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace System.Collections.Immutable
{
    internal static partial class ImmutableExtensions
    {
        internal static bool TryGetCount<T>(this IEnumerable<T> sequence, out int count)
        {
            return TryGetCount<T>((IEnumerable)sequence, out count);
        }

        internal static bool TryGetCount<T>(this IEnumerable sequence, out int count)
        {
            if (sequence is ICollection collection)
            {
                count = collection.Count;
                return true;
            }

            if (sequence is ICollection<T> collectionOfT)
            {
                count = collectionOfT.Count;
                return true;
            }

            if (sequence is IReadOnlyCollection<T> readOnlyCollection)
            {
                count = readOnlyCollection.Count;
                return true;
            }

            count = 0;
            return false;
        }

        internal static int GetCount<T>(ref IEnumerable<T> sequence)
        {
            if (sequence.TryGetCount(out var count))
            {
                return count;
            }

            var list = new List<T>();
            foreach (var item in sequence)
            {
                list.Add(item);
            }

            count = list.Count;
            sequence = list;
            return count;
        }

        internal static bool TryCopyTo<T>(this IEnumerable<T> sequence, T[] array, int arrayIndex)
        {
            Debug.Assert(sequence != null);
            Debug.Assert(array != null);
            Debug.Assert(arrayIndex >= 0 && arrayIndex <= array.Length);

            if (sequence is IList<T>)
            {
                if (sequence is List<T> list)
                {
                    list.CopyTo(array, arrayIndex);
                    return true;
                }

                if (sequence.GetType() == typeof(T[]))
                {
                    var sourceArray = (T[])sequence;
                    Array.Copy(sourceArray, 0, array, arrayIndex, sourceArray.Length);
                    return true;
                }

                if (sequence is ImmutableArray<T> immutable)
                {
                    Array.Copy(immutable.array!, 0, array, arrayIndex, immutable.Length);
                    return true;
                }
            }

            return false;
        }

        internal static T[] ToArray<T>(this IEnumerable<T> sequence, int count)
        {
            Requires.NotNull(sequence, nameof(sequence));
            Requires.Range(count >= 0, nameof(count));

            if (count == 0)
            {
                return ImmutableArray<T>.Empty.array!;
            }

            var array = new T[count];
            if (!sequence.TryCopyTo(array, 0))
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    Requires.Argument(index < count);
                    array[index++] = item;
                }

                Requires.Argument(index == count);
            }

            return array;
        }
    }
}
