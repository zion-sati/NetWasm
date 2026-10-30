// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Collections.Immutable
// ImmutableExtensions.Minimal.cs at commit
// 474ed963699fc29809015392738a15231aaae2de. LINQ's ToList fallback is kept
// as an explicit enumeration so this CoreLib helper has no LINQ dependency.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace System.Collections.Immutable
{
    internal static partial class ImmutableExtensions
    {
        internal static bool IsValueType<T>()
        {
            return typeof(T).IsValueType;
        }

        internal static IReadOnlyList<T> AsReadOnlyList<T>(this IEnumerable<T> sequence)
        {
            Requires.NotNull(sequence, nameof(sequence));

            if (sequence is IReadOnlyList<T> orderedCollection)
            {
                return orderedCollection;
            }

            if (sequence is IList<T> listOfT)
            {
                return new ListOfTWrapper<T>(listOfT);
            }

            return new FallbackWrapper<T>(sequence);
        }

        internal static DisposableEnumeratorAdapter<T, TEnumerator> GetEnumerableDisposable<T, TEnumerator>(this IEnumerable<T> enumerable)
            where TEnumerator : struct, IStrongEnumerator<T>, IEnumerator<T>
        {
            Requires.NotNull(enumerable, nameof(enumerable));

            if (enumerable is IStrongEnumerable<T, TEnumerator> strongEnumerable)
            {
                return new DisposableEnumeratorAdapter<T, TEnumerator>(strongEnumerable.GetEnumerator());
            }
            else
            {
                return new DisposableEnumeratorAdapter<T, TEnumerator>(enumerable.GetEnumerator());
            }
        }

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

            T[] array = new T[count];
            if (!sequence.TryCopyTo(array, 0))
            {
                int i = 0;
                foreach (T item in sequence)
                {
                    Requires.Argument(i < count);
                    array[i++] = item;
                }

                Requires.Argument(i == count);
            }

            return array;
        }

        /// <summary>
        /// Clears the specified stack. For empty stacks, this avoids the call to
        /// <see cref="Stack{T}.Clear"/>.
        /// </summary>
        internal static void ClearFastWhenEmpty<T>(this Stack<T> stack)
        {
            if (stack.Count > 0)
            {
                stack.Clear();
            }
        }

        private sealed class ListOfTWrapper<T> : IReadOnlyList<T>
        {
            private readonly IList<T> _collection;

            internal ListOfTWrapper(IList<T> collection)
            {
                Requires.NotNull(collection, nameof(collection));
                _collection = collection;
            }

            public int Count
            {
                get { return _collection.Count; }
            }

            public T this[int index]
            {
                get { return _collection[index]; }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _collection.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        private sealed class FallbackWrapper<T> : IReadOnlyList<T>
        {
            private readonly IEnumerable<T> _sequence;
            private IList<T>? _collection;

            internal FallbackWrapper(IEnumerable<T> sequence)
            {
                Requires.NotNull(sequence, nameof(sequence));
                _sequence = sequence;
            }

            public int Count
            {
                get
                {
                    if (_collection == null)
                    {
                        if (_sequence.TryGetCount(out int count))
                        {
                            return count;
                        }

                        _collection = new List<T>(_sequence);
                    }

                    return _collection.Count;
                }
            }

            public T this[int index]
            {
                get
                {
                    _collection ??= new List<T>(_sequence);

                    return _collection[index];
                }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _sequence.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }
    }
}
