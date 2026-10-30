// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.ObjectModel
{
    [CollectionBuilder(typeof(ReadOnlyCollection), "CreateSet")]
    public class ReadOnlySet<T> : IReadOnlySet<T>, ISet<T>, ICollection
    {
        private readonly ISet<T> _set;

        public ReadOnlySet(ISet<T> set)
        {
            ArgumentNullException.ThrowIfNull(set);
            _set = set;
        }

        public static ReadOnlySet<T> Empty { get; } = new ReadOnlySet<T>(new HashSet<T>());

        protected ISet<T> Set => _set;

        public int Count => _set.Count;

        public IEnumerator<T> GetEnumerator() =>
            _set.Count == 0 ? ((IEnumerable<T>)[]).GetEnumerator() :
            _set.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(T item) => _set.Contains(item);

        public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);

        public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);

        void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => CollectionHelpers.CopyTo(_set, array, index);

        bool ICollection<T>.IsReadOnly => true;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => _set is ICollection c ? c.SyncRoot : this;

        bool ISet<T>.Add(T item) => throw new NotSupportedException();

        void ISet<T>.ExceptWith(IEnumerable<T> other) => throw new NotSupportedException();

        void ISet<T>.IntersectWith(IEnumerable<T> other) => throw new NotSupportedException();

        void ISet<T>.SymmetricExceptWith(IEnumerable<T> other) => throw new NotSupportedException();

        void ISet<T>.UnionWith(IEnumerable<T> other) => throw new NotSupportedException();

        void ICollection<T>.Add(T item) => throw new NotSupportedException();

        void ICollection<T>.Clear() => throw new NotSupportedException();

        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
    }
}
