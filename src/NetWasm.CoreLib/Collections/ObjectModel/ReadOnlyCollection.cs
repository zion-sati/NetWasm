// Adapted from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.ObjectModel
{
    [CollectionBuilder(typeof(ReadOnlyCollection), "CreateCollection")]
    public class ReadOnlyCollection<T> : IList<T>, IList, IReadOnlyList<T>
    {
        private readonly IList<T> list;

        public ReadOnlyCollection(IList<T> list)
        {
            if (list == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.list);
            }
            this.list = list;
        }

        public static ReadOnlyCollection<T> Empty { get; } = new ReadOnlyCollection<T>(Array.Empty<T>());

        public int Count => list.Count;

        public T this[int index] => list[index];

        public bool Contains(T value)
        {
            return list.Contains(value);
        }

        public void CopyTo(T[] array, int index)
        {
            list.CopyTo(array, index);
        }

        public IEnumerator<T> GetEnumerator() =>
            list.Count == 0 ? SZGenericArrayEnumerator<T>.Empty :
            list.GetEnumerator();

        public int IndexOf(T value)
        {
            return list.IndexOf(value);
        }

        protected IList<T> Items => list;

        bool ICollection<T>.IsReadOnly => true;

        T IList<T>.this[int index]
        {
            get => list[index];
            set => ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void ICollection<T>.Add(T value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void ICollection<T>.Clear()
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void IList<T>.Insert(int index, T value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        bool ICollection<T>.Remove(T value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
            return false;
        }

        void IList<T>.RemoveAt(int index)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)list).GetEnumerator();
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => list is ICollection coll ? coll.SyncRoot : this;

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            if (array.Rank != 1)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
            }

            if (array.GetLowerBound(0) != 0)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
            }

            if (index < 0)
            {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count)
            {
                ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            }

            if (array is T[] items)
            {
                list.CopyTo(items, index);
            }
            else
            {
                object?[]? objects = array as object[];
                if (objects == null)
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }

                int count = list.Count;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        objects[index++] = list[i];
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    ThrowHelper.ThrowArgumentException_Argument_IncompatibleArrayType();
                }
            }
        }

        bool IList.IsFixedSize => true;

        bool IList.IsReadOnly => true;

        object? IList.this[int index]
        {
            get => list[index];
            set => ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        int IList.Add(object? value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
            return -1;
        }

        void IList.Clear()
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        private static bool IsCompatibleObject(object? value)
        {
            return (value is T) || (value == null && default(T) == null);
        }

        bool IList.Contains(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return Contains((T)value!);
            }
            return false;
        }

        int IList.IndexOf(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return IndexOf((T)value!);
            }
            return -1;
        }

        void IList.Insert(int index, object? value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void IList.Remove(object? value)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }

        void IList.RemoveAt(int index)
        {
            ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ReadOnlyCollection);
        }
    }

    public static class ReadOnlyCollection
    {
        public static ReadOnlyCollection<T> CreateCollection<T>(params ReadOnlySpan<T> values) =>
            values.IsEmpty ? ReadOnlyCollection<T>.Empty : new ReadOnlyCollection<T>(values.ToArray());

        public static ReadOnlySet<T> CreateSet<T>(params ReadOnlySpan<T> values)
        {
            if (values.IsEmpty)
            {
                return ReadOnlySet<T>.Empty;
            }

            HashSet<T> hashSet = [];
            foreach (T value in values)
            {
                hashSet.Add(value);
            }

            return new ReadOnlySet<T>(hashSet);
        }
    }
}
