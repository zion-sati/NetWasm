// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib HashSet.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    public class HashSet<T> : ISet<T>, IReadOnlySet<T>
    {
        private readonly Dictionary<T, bool> _items;
        private bool _hasNull;
        private int _version;

        public HashSet() : this(0, null)
        {
        }

        public HashSet(IEqualityComparer<T>? comparer) : this(0, comparer)
        {
        }

        public HashSet(int capacity) : this(capacity, null)
        {
        }

        public HashSet(int capacity, IEqualityComparer<T>? comparer)
        {
            _items = new Dictionary<T, bool>(capacity, comparer);
        }

        public HashSet(IEnumerable<T> collection) : this(collection, null)
        {
        }

        public HashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) :
            this(collection == null ? throw new System.ArgumentNullException() : 0, comparer)
        {
            if (collection is ICollection<T> sized)
            {
                _items.EnsureCapacity(sized.Count);
            }
            foreach (var item in collection)
            {
                Add(item);
            }
        }

        public int Count
        {
            get => _items.Count + (_hasNull ? 1 : 0);
        }

        public int Capacity
        {
            get => _items.Capacity;
        }

        public bool IsReadOnly => false;
        public IEqualityComparer<T> Comparer
        {
            get => _items.Comparer;
        }

        public bool Add(T item)
        {
            if (item is null)
            {
                if (_hasNull || TryGetNullEquivalent(out _))
                {
                    return false;
                }

                _hasNull = true;
                _version++;
                return true;
            }

            if (_hasNull && IsNullEquivalent(item))
            {
                return false;
            }

            if (!_items.TryAdd(item, true))
            {
                return false;
            }

            _version++;
            return true;
        }

        void ICollection<T>.Add(T item) => Add(item);
        public bool Contains(T item) => item is null
            ? _hasNull || TryGetNullEquivalent(out _)
            : _items.ContainsKey(item) || _hasNull && IsNullEquivalent(item);

        public bool Remove(T item)
        {
            var removed = item is null ? RemoveNullEquivalent() : RemoveNonNull(item);
            if (removed)
            {
                _version++;
            }

            return removed;
        }

        public void Clear()
        {
            if (Count == 0)
            {
                return;
            }

            _items.Clear();
            _hasNull = false;
            _version++;
        }

        public int EnsureCapacity(int capacity) => _items.EnsureCapacity(capacity);

        public void TrimExcess() => _items.TrimExcess();

        public void TrimExcess(int capacity) => _items.TrimExcess(capacity);

        public void CopyTo(T[] array) => CopyTo(array, 0, Count);

        public void CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);

        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            if (array == null)
            {
                throw new System.ArgumentNullException();
            }
            if (arrayIndex < 0 || arrayIndex > array.Length)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (count < 0 || count > Count || count > array.Length - arrayIndex)
            {
                throw new System.ArgumentException();
            }
            var copied = 0;
            foreach (var item in this)
            {
                if (copied == count)
                {
                    break;
                }
                array[arrayIndex + copied++] = item;
            }
        }

        public bool TryGetValue(T equalValue, out T actualValue)
        {
            if (equalValue is null)
            {
                if (_hasNull)
                {
                    actualValue = default!;
                    return true;
                }

                return TryGetNullEquivalent(out actualValue);
            }

            if (_items.TryGetKey(equalValue, out actualValue))
            {
                return true;
            }

            if (_hasNull && IsNullEquivalent(equalValue))
            {
                actualValue = default!;
                return true;
            }

            return false;
        }

        public void UnionWith(IEnumerable<T> other)
        {
            ValidateOther(other);
            foreach (var item in other) Add(item);
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            ValidateOther(other);
            var retained = new HashSet<T>(Comparer);
            foreach (var item in other)
                if (Contains(item)) retained.Add(item);
            ReplaceWith(retained);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            ValidateOther(other);
            foreach (var item in other) Remove(item);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            ValidateOther(other);
            var unique = new HashSet<T>(other, Comparer);
            foreach (var item in unique)
                if (!Remove(item)) Add(item);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            ValidateOther(other);
            var set = CopyOf(other);
            foreach (var item in this)
                if (!set.Contains(item)) return false;
            return true;
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            ValidateOther(other);
            var set = CopyOf(other);
            return Count < set.Count && IsSubsetOf(set);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            ValidateOther(other);
            foreach (var item in other)
                if (!Contains(item)) return false;
            return true;
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            ValidateOther(other);
            var set = CopyOf(other);
            return Count > set.Count && IsSupersetOf(set);
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            ValidateOther(other);
            var set = CopyOf(other);
            return Count == set.Count && IsSubsetOf(set);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            ValidateOther(other);
            foreach (var item in other)
                if (Contains(item)) return true;
            return false;
        }

        public int RemoveWhere(System.Predicate<T> match)
        {
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            var removed = new List<T>();
            foreach (var item in this)
                if (match(item)) removed.Add(item);
            foreach (var item in removed) Remove(item);
            return removed.Count;
        }

        public static IEqualityComparer<HashSet<T>> CreateSetComparer() => new SetEqualityComparer();

        public virtual void OnDeserialization(object? sender)
        {
        }

        public AlternateLookup<TAlternate> GetAlternateLookup<TAlternate>()
            where TAlternate : allows ref struct =>
            new AlternateLookup<TAlternate>(this, GetAlternateComparer<TAlternate>());

        public bool TryGetAlternateLookup<TAlternate>(out AlternateLookup<TAlternate> lookup)
            where TAlternate : allows ref struct
        {
            if (_items.Comparer is IAlternateEqualityComparer<TAlternate, T> comparer)
            {
                lookup = new AlternateLookup<TAlternate>(this, comparer);
                return true;
            }
            lookup = default;
            return false;
        }

        private IAlternateEqualityComparer<TAlternate, T> GetAlternateComparer<TAlternate>()
            where TAlternate : allows ref struct =>
            _items.Comparer as IAlternateEqualityComparer<TAlternate, T>
                ?? throw new System.InvalidOperationException();

        public Enumerator GetEnumerator() => new(this, _items.GetEnumerator());
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private void ReplaceWith(HashSet<T> replacement)
        {
            Clear();
            foreach (var item in replacement) Add(item);
        }

        private HashSet<T> CopyOf(IEnumerable<T> source)
        {
            var result = new HashSet<T>(Comparer);
            foreach (var item in source) result.Add(item);
            return result;
        }

        private static void ValidateOther(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new System.ArgumentNullException();
            }
        }

        private bool RemoveNullEquivalent()
        {
            if (_hasNull)
            {
                _hasNull = false;
                return true;
            }

            return TryGetNullEquivalent(out var actual) && _items.Remove(actual);
        }

        private bool RemoveNonNull(T item)
        {
            if (_items.Remove(item))
            {
                return true;
            }

            if (!_hasNull || !IsNullEquivalent(item))
            {
                return false;
            }

            _hasNull = false;
            return true;
        }

        private bool TryGetNullEquivalent(out T actual)
        {
            foreach (var item in _items.Keys)
            {
                if (IsNullEquivalent(item))
                {
                    actual = item;
                    return true;
                }
            }

            actual = default!;
            return false;
        }

        private bool IsNullEquivalent(T item) =>
            Comparer.GetHashCode(item) == 0 && Comparer.Equals(item, default!);

        private sealed class SetEqualityComparer : EqualityComparer<HashSet<T>>
        {
            public override bool Equals(HashSet<T>? left, HashSet<T>? right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null || left.Count != right.Count) return false;
                return left.IsSubsetOf(right);
            }

            public override int GetHashCode(HashSet<T> set)
            {
                if (set is null) return 0;
                var hash = 0;
                foreach (var item in set) hash ^= item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);
                return hash;
            }
        }

        public readonly struct AlternateLookup<TAlternate>
            where TAlternate : allows ref struct
        {
            private readonly HashSet<T> _set;
            private readonly IAlternateEqualityComparer<TAlternate, T> _comparer;

            internal AlternateLookup(
                HashSet<T> set,
                IAlternateEqualityComparer<TAlternate, T> comparer)
            {
                _set = set;
                _comparer = comparer;
            }

            public HashSet<T> Set
            {
                get => _set;
            }
            public bool Add(TAlternate item)
            {
                if (Contains(item)) return false;
                return _set.Add(_comparer.Create(item));
            }

            public bool Contains(TAlternate item)
            {
                foreach (var actual in _set)
                    if (_comparer.Equals(item, actual)) return true;
                return false;
            }

            public bool Remove(TAlternate item)
            {
                foreach (var actual in _set)
                {
                    if (_comparer.Equals(item, actual)) return _set.Remove(actual);
                }
                return false;
            }

            public bool TryGetValue(TAlternate equalValue, out T actualValue) =>
                TryFind(equalValue, out actualValue);

            private bool TryFind(TAlternate equalValue, out T actualValue)
            {
                foreach (var actual in _set)
                {
                    if (_comparer.Equals(equalValue, actual))
                    {
                        actualValue = actual;
                        return true;
                    }
                }
                actualValue = default!;
                return false;
            }
        }

        public struct Enumerator : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
        {
            private readonly HashSet<T> _set;
            private readonly int _version;
            private Dictionary<T, bool>.Enumerator _inner;
            private bool _nullPending;
            private bool _currentIsNull;

            internal Enumerator(HashSet<T> set, Dictionary<T, bool>.Enumerator inner)
            {
                _set = set;
                _version = set._version;
                _inner = inner;
                _nullPending = set._hasNull;
                _currentIsNull = false;
            }

            public T Current
            {
                get => _currentIsNull ? default! : _inner.Current.Key;
            }

            object System.Collections.IEnumerator.Current => Current!;

            public bool MoveNext()
            {
                if (_version != _set._version)
                {
                    throw new System.InvalidOperationException();
                }

                if (_nullPending)
                {
                    _nullPending = false;
                    _currentIsNull = true;
                    return true;
                }

                _currentIsNull = false;
                return _inner.MoveNext();
            }

            public void Dispose() => _inner.Dispose();
            void System.Collections.IEnumerator.Reset() => throw new System.NotSupportedException();
        }
    }
}
