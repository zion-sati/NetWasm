// Portions derived from dotnet/runtime System.Private.CoreLib's ReadOnlySet<T>
// at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements; the .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Collections.ObjectModel
{
    public class ReadOnlySet<T> : Collections.Generic.ICollection<T>,
        Collections.Generic.IEnumerable<T>, Collections.Generic.IReadOnlyCollection<T>,
        Collections.Generic.IReadOnlySet<T>, Collections.Generic.ISet<T>,
        Collections.ICollection, Collections.IEnumerable
    {
        private readonly Collections.Generic.ISet<T> _set;

        public ReadOnlySet(Collections.Generic.ISet<T> set)
        {
            _set = set ?? throw new ArgumentNullException();
        }

        public static ReadOnlySet<T> Empty { get; } =
            new(CreateMutableSet());

        protected Collections.Generic.ISet<T> Set => _set;
        public int Count { get => _set.Count; }
        bool Collections.Generic.ICollection<T>.IsReadOnly => true;
        public bool Contains(T item) => _set.Contains(item);
        public void CopyTo(T[] array, int index) => _set.CopyTo(array, index);
        public Collections.Generic.IEnumerator<T> GetEnumerator() => _set.GetEnumerator();
        Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        bool Collections.Generic.ISet<T>.Add(T item) => ThrowReadOnly<bool>();
        void Collections.Generic.ICollection<T>.Add(T item) => ThrowReadOnly();
        void Collections.Generic.ICollection<T>.Clear() => ThrowReadOnly();
        bool Collections.Generic.ICollection<T>.Remove(T item) => ThrowReadOnly<bool>();
        void Collections.Generic.ISet<T>.UnionWith(Collections.Generic.IEnumerable<T> other) => ThrowReadOnly();
        void Collections.Generic.ISet<T>.IntersectWith(Collections.Generic.IEnumerable<T> other) => ThrowReadOnly();
        void Collections.Generic.ISet<T>.ExceptWith(Collections.Generic.IEnumerable<T> other) => ThrowReadOnly();
        void Collections.Generic.ISet<T>.SymmetricExceptWith(Collections.Generic.IEnumerable<T> other) => ThrowReadOnly();

        public bool IsSubsetOf(Collections.Generic.IEnumerable<T> other) => _set.IsSubsetOf(other);
        public bool IsSupersetOf(Collections.Generic.IEnumerable<T> other) => _set.IsSupersetOf(other);
        public bool IsProperSupersetOf(Collections.Generic.IEnumerable<T> other) => _set.IsProperSupersetOf(other);
        public bool IsProperSubsetOf(Collections.Generic.IEnumerable<T> other) => _set.IsProperSubsetOf(other);
        public bool Overlaps(Collections.Generic.IEnumerable<T> other) => _set.Overlaps(other);
        public bool SetEquals(Collections.Generic.IEnumerable<T> other) => _set.SetEquals(other);

        bool Collections.ICollection.IsSynchronized => false;
        object Collections.ICollection.SyncRoot => this;
        void Collections.ICollection.CopyTo(Array array, int index)
        {
            if (array is T[] typed)
            {
                ((Collections.Generic.ICollection<T>)this).CopyTo(typed, index);
                return;
            }
            throw new ArgumentException();
        }

        private static void ThrowReadOnly() => throw new NotSupportedException();
        private static TResult ThrowReadOnly<TResult>() => throw new NotSupportedException();

        internal static Collections.Generic.ISet<T> CreateMutableSet() => new MutableSet();

        private sealed class MutableSet : Collections.Generic.ISet<T>
        {
            private readonly Collections.Generic.List<T> _items = new();
            public int Count => _items.Count;
            public bool IsReadOnly => false;
            public bool Add(T item)
            {
                if (_items.Contains(item)) return false;
                _items.Add(item);
                return true;
            }
            void Collections.Generic.ICollection<T>.Add(T item) => Add(item);
            public void Clear() => _items.Clear();
            public bool Contains(T item) => _items.Contains(item);
            public void CopyTo(T[] array, int index) => _items.CopyTo(array, index);
            public bool Remove(T item) => _items.Remove(item);
            public Collections.Generic.IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
            Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            public void UnionWith(Collections.Generic.IEnumerable<T> other) { foreach (var item in other) Add(item); }
            public void IntersectWith(Collections.Generic.IEnumerable<T> other)
            {
                var keep = new Collections.Generic.List<T>();
                foreach (var item in other) if (Contains(item)) keep.Add(item);
                _items.Clear(); foreach (var item in keep) _items.Add(item);
            }
            public void ExceptWith(Collections.Generic.IEnumerable<T> other) { foreach (var item in other) Remove(item); }
            public void SymmetricExceptWith(Collections.Generic.IEnumerable<T> other)
            {
                foreach (var item in other) if (!Remove(item)) Add(item);
            }
            public bool IsSubsetOf(Collections.Generic.IEnumerable<T> other)
            {
                var candidate = new MutableSet(); candidate.UnionWith(other);
                foreach (var item in _items) if (!candidate.Contains(item)) return false;
                return true;
            }
            public bool IsSupersetOf(Collections.Generic.IEnumerable<T> other)
            { foreach (var item in other) if (!Contains(item)) return false; return true; }
            public bool IsProperSupersetOf(Collections.Generic.IEnumerable<T> other)
            { var candidate = new MutableSet(); candidate.UnionWith(other); return Count > candidate.Count && IsSupersetOf(candidate); }
            public bool IsProperSubsetOf(Collections.Generic.IEnumerable<T> other)
            { var candidate = new MutableSet(); candidate.UnionWith(other); return Count < candidate.Count && IsSubsetOf(candidate); }
            public bool Overlaps(Collections.Generic.IEnumerable<T> other)
            { foreach (var item in other) if (Contains(item)) return true; return false; }
            public bool SetEquals(Collections.Generic.IEnumerable<T> other)
            { var candidate = new MutableSet(); candidate.UnionWith(other); return Count == candidate.Count && IsSubsetOf(candidate); }
        }
    }
}
