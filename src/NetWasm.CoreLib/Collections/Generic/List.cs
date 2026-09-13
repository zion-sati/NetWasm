// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic
{
    // Portions derived from dotnet/runtime System.Private.CoreLib's List<T>
    // at commit 811225a482702af7ecc35d817966bc70b88a3a23.
    public class List<T> : ICollection<T>, IEnumerable<T>, IList<T>, IReadOnlyCollection<T>,
        IReadOnlyList<T>, System.Collections.ICollection, System.Collections.IEnumerable,
        System.Collections.IList
    {
        private T[] _items;
        private int _count;
        private int _version;

        public List()
        {
            _items = new T[0];
        }

        public List(int capacity)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            _items = new T[capacity];
        }

        public List(IEnumerable<T> collection) : this()
        {
            if (collection == null)
            {
                throw new System.ArgumentNullException();
            }
            AddRange(collection);
        }

        public int Count { get => _count; }

        // CollectionsMarshal.AsSpan uses this bridge to preserve the upstream
        // writable view over the active portion of List<T>'s backing array.
        internal T[] Items => _items;
        internal int Size => _count;

        public System.Collections.ObjectModel.ReadOnlyCollection<T> AsReadOnly() =>
            new(this);

        public int BinarySearch(T item) => BinarySearch(0, _count, item, null);

        public int BinarySearch(T item, IComparer<T>? comparer) =>
            BinarySearch(0, _count, item, comparer);

        public int BinarySearch(int index, int count, T item, IComparer<T>? comparer)
        {
            ValidateRange(index, count);
            comparer ??= Comparer<T>.Default;
            var low = index;
            var high = index + count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var comparison = comparer.Compare(_items[middle], item);
                if (comparison == 0)
                {
                    return middle;
                }
                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return ~low;
        }

        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _count)
                {
                    throw new System.ArgumentOutOfRangeException();
                }
                if (value == _items.Length)
                {
                    return;
                }
                var replacement = new T[value];
                System.Array.Copy(_items, 0, replacement, 0, _count);
                _items = replacement;
            }
        }

        public bool IsReadOnly => false;

        public T this[int index]
        {
            get
            {
                ValidateIndex(index);
                return _items[index];
            }
            set
            {
                ValidateIndex(index);
                _items[index] = value;
                _version++;
            }
        }

        public void Add(T item)
        {
            EnsureCapacity(_count + 1);
            _items[_count] = item;
            _count++;
            _version++;
        }

        public void Clear()
        {
            for (var index = 0; index < _count; index++)
            {
                _items[index] = default!;
            }
            _count = 0;
            _version++;
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public List<TOutput> ConvertAll<TOutput>(System.Converter<T, TOutput> converter)
        {
            if (converter == null)
            {
                throw new System.ArgumentNullException();
            }
            var result = new List<TOutput>(_count);
            for (var index = 0; index < _count; index++)
            {
                result.Add(converter(_items[index]));
            }
            return result;
        }

        public int IndexOf(T item)
        {
            return IndexOf(item, 0, _count);
        }

        public int IndexOf(T item, int index)
        {
            return IndexOf(item, index, _count - index);
        }

        public int IndexOf(T item, int index, int count)
        {
            ValidateRange(index, count);
            for (var current = index; current < index + count; current++)
            {
                if (EqualityComparer<T>.Default.Equals(_items[current], item))
                {
                    return current;
                }
            }
            return -1;
        }

        public int LastIndexOf(T item)
        {
            return LastIndexOf(item, _count - 1, _count);
        }

        public int LastIndexOf(T item, int index)
        {
            return LastIndexOf(item, index, index + 1);
        }

        public int LastIndexOf(T item, int index, int count)
        {
            if (index < -1 || index >= _count || count < 0 || index - count + 1 < 0)
            {
                if (index == -1 && count == 0)
                {
                    return -1;
                }
                throw new System.ArgumentOutOfRangeException();
            }
            for (var current = index; current >= index - count + 1; current--)
            {
                if (EqualityComparer<T>.Default.Equals(_items[current], item))
                {
                    return current;
                }
            }
            return -1;
        }

        public T? Find(System.Predicate<T> match)
        {
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            for (var index = 0; index < _count; index++)
            {
                if (match(_items[index]))
                {
                    return _items[index];
                }
            }
            return default;
        }

        public bool Exists(System.Predicate<T> match)
        {
            return FindIndex(match) >= 0;
        }

        public int FindIndex(System.Predicate<T> match)
        {
            return FindIndex(0, _count, match);
        }

        public int FindIndex(int startIndex, System.Predicate<T> match)
        {
            return FindIndex(startIndex, _count - startIndex, match);
        }

        public int FindIndex(int startIndex, int count, System.Predicate<T> match)
        {
            ValidateRange(startIndex, count);
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            for (var index = startIndex; index < startIndex + count; index++)
            {
                if (match(_items[index]))
                {
                    return index;
                }
            }
            return -1;
        }

        public List<T> FindAll(System.Predicate<T> match)
        {
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            var result = new List<T>();
            for (var index = 0; index < _count; index++)
            {
                if (match(_items[index]))
                {
                    result.Add(_items[index]);
                }
            }
            return result;
        }

        public T? FindLast(System.Predicate<T> match)
        {
            var index = FindLastIndex(match);
            return index < 0 ? default : _items[index];
        }

        public int FindLastIndex(System.Predicate<T> match)
        {
            return FindLastIndex(_count - 1, _count, match);
        }

        public int FindLastIndex(int startIndex, System.Predicate<T> match)
        {
            return FindLastIndex(startIndex, startIndex + 1, match);
        }

        public int FindLastIndex(int startIndex, int count, System.Predicate<T> match)
        {
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            if (startIndex < -1 || startIndex >= _count || count < 0 ||
                startIndex - count + 1 < 0)
            {
                if (startIndex == -1 && count == 0)
                {
                    return -1;
                }
                throw new System.ArgumentOutOfRangeException();
            }
            for (var index = startIndex; index >= startIndex - count + 1; index--)
            {
                if (match(_items[index]))
                {
                    return index;
                }
            }
            return -1;
        }

        public void ForEach(System.Action<T> action)
        {
            if (action == null)
            {
                throw new System.ArgumentNullException();
            }
            for (var index = 0; index < _count; index++)
            {
                action(_items[index]);
            }
        }

        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)_count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            EnsureCapacity(_count + 1);
            for (var current = _count; current > index; current--)
            {
                _items[current] = _items[current - 1];
            }
            _items[index] = item;
            _count++;
            _version++;
        }

        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            ValidateIndex(index);
            _count--;
            for (var current = index; current < _count; current++)
            {
                _items[current] = _items[current + 1];
            }
            _items[_count] = default!;
            _version++;
        }

        public void RemoveRange(int index, int count)
        {
            ValidateRange(index, count);
            if (count == 0)
            {
                return;
            }
            var newCount = _count - count;
            for (var current = index; current < newCount; current++)
            {
                _items[current] = _items[current + count];
            }
            for (var current = newCount; current < _count; current++)
            {
                _items[current] = default!;
            }
            _count = newCount;
            _version++;
        }

        public int RemoveAll(System.Predicate<T> match)
        {
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            var destination = 0;
            for (var source = 0; source < _count; source++)
            {
                if (!match(_items[source]))
                {
                    _items[destination++] = _items[source];
                }
            }
            var removed = _count - destination;
            if (removed == 0)
            {
                return 0;
            }
            for (var index = destination; index < _count; index++)
            {
                _items[index] = default!;
            }
            _count = destination;
            _version++;
            return removed;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new System.ArgumentNullException();
            }
            if (arrayIndex < 0 || arrayIndex > array.Length - _count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            for (var index = 0; index < _count; index++)
            {
                array[arrayIndex + index] = _items[index];
            }
        }

        public void CopyTo(T[] array)
        {
            CopyTo(0, array, 0, _count);
        }

        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            ValidateRange(index, count);
            if (array == null)
            {
                throw new System.ArgumentNullException();
            }
            if (arrayIndex < 0 || arrayIndex > array.Length - count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            for (var offset = 0; offset < count; offset++)
            {
                array[arrayIndex + offset] = _items[index + offset];
            }
        }

        public Enumerator GetEnumerator() => new(this);

        public T[] ToArray()
        {
            var result = new T[_count];
            System.Array.Copy(_items, 0, result, 0, _count);
            return result;
        }

        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new System.ArgumentNullException();
            }
            var source = System.Object.ReferenceEquals(collection, this)
                ? ToArray()
                : collection;
            foreach (var item in source)
            {
                Add(item);
            }
        }

        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if ((uint)index > (uint)_count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (collection == null)
            {
                throw new System.ArgumentNullException();
            }
            var values = new List<T>();
            foreach (var item in collection)
            {
                values.Add(item);
            }
            if (values.Count == 0)
            {
                return;
            }
            EnsureCapacity(_count + values.Count);
            for (var current = _count - 1; current >= index; current--)
            {
                _items[current + values.Count] = _items[current];
            }
            for (var current = 0; current < values.Count; current++)
            {
                _items[index + current] = values[current];
            }
            _count += values.Count;
            _version++;
        }

        public List<T> GetRange(int index, int count)
        {
            ValidateRange(index, count);
            var result = new List<T>(count);
            for (var current = 0; current < count; current++)
            {
                result.Add(_items[index + current]);
            }
            return result;
        }

        public void Reverse()
        {
            Reverse(0, _count);
        }

        public void Reverse(int index, int count)
        {
            ValidateRange(index, count);
            for (var left = index; left < index + (count / 2); left++)
            {
                var right = index + count - left - 1;
                var value = _items[left];
                _items[left] = _items[right];
                _items[right] = value;
            }
            _version++;
        }

        public List<T> Slice(int start, int length) => GetRange(start, length);

        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            EnsureCapacityCore(capacity);
            return _items.Length;
        }

        public void TrimExcess()
        {
            if (_count < _items.Length)
            {
                Capacity = _count;
            }
        }

        public void Sort() => Sort(Comparer<T>.Default);

        public void Sort(IComparer<T>? comparer)
        {
            Sort(0, _count, comparer);
        }

        public void Sort(int index, int count, IComparer<T>? comparer)
        {
            ValidateRange(index, count);
            comparer ??= Comparer<T>.Default;
            for (var current = index + 1; current < index + count; current++)
            {
                var value = _items[current];
                var insertion = current;
                while (insertion > index && comparer.Compare(_items[insertion - 1], value) > 0)
                {
                    _items[insertion] = _items[insertion - 1];
                    insertion--;
                }
                _items[insertion] = value;
            }
            _version++;
        }

        public void Sort(System.Comparison<T> comparison)
        {
            if (comparison == null)
            {
                throw new System.ArgumentNullException();
            }
            Sort(0, _count, new ComparisonComparer(comparison));
        }

        public bool TrueForAll(System.Predicate<T> match)
        {
            if (match == null)
            {
                throw new System.ArgumentNullException();
            }
            for (var index = 0; index < _count; index++)
            {
                if (!match(_items[index]))
                {
                    return false;
                }
            }
            return true;
        }

        object? System.Collections.IList.this[int index]
        {
            get => this[index];
            set => this[index] = ConvertObject(value);
        }

        int System.Collections.IList.Add(object? value)
        {
            Add(ConvertObject(value));
            return _count - 1;
        }

        bool System.Collections.IList.Contains(object? value) =>
            IsCompatibleObject(value) && Contains(ConvertObject(value));

        int System.Collections.IList.IndexOf(object? value) =>
            IsCompatibleObject(value) ? IndexOf(ConvertObject(value)) : -1;

        void System.Collections.IList.Insert(int index, object? value) =>
            Insert(index, ConvertObject(value));

        void System.Collections.IList.Remove(object? value)
        {
            if (IsCompatibleObject(value))
            {
                Remove(ConvertObject(value));
            }
        }

        bool System.Collections.IList.IsReadOnly => false;
        bool System.Collections.IList.IsFixedSize => false;

        int System.Collections.ICollection.Count => _count;
        bool System.Collections.ICollection.IsSynchronized => false;
        object System.Collections.ICollection.SyncRoot => this;

        void System.Collections.ICollection.CopyTo(System.Array array, int index)
        {
            if (array is T[] typed)
            {
                CopyTo(typed, index);
                return;
            }
            throw new System.ArgumentException();
        }

        internal int Version => _version;

        public struct Enumerator : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
        {
            private readonly List<T> _list;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(List<T> list)
            {
                _list = list;
                _version = list._version;
                _index = 0;
                _current = default!;
            }

            public T Current { get => _current; }
            object System.Collections.IEnumerator.Current => _current!;

            public bool MoveNext()
            {
                if (_version != _list._version)
                {
                    throw new System.InvalidOperationException();
                }
                if (_index == _list._count)
                {
                    _current = default!;
                    return false;
                }
                _current = _list._items[_index++];
                return true;
            }

            public void Dispose()
            {
            }

            void System.Collections.IEnumerator.Reset() =>
                throw new System.NotSupportedException();
        }

        private sealed class ComparisonComparer(System.Comparison<T> comparison) : IComparer<T>
        {
            public int Compare(T? left, T? right) => comparison(left!, right!);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        private void EnsureCapacityCore(int required)
        {
            if (required <= _items.Length)
            {
                return;
            }
            var capacity = _items.Length == 0 ? 4 : _items.Length * 2;
            if (capacity < required)
            {
                capacity = required;
            }
            var replacement = new T[capacity];
            for (var index = 0; index < _count; index++)
            {
                replacement[index] = _items[index];
            }
            _items = replacement;
        }

        private void ValidateIndex(int index)
        {
            if ((uint)index >= (uint)_count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
        }

        private void ValidateRange(int index, int count)
        {
            if (index < 0 || count < 0 || index > _count - count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
        }

        private static bool IsCompatibleObject(object? value) =>
            value is T || (value is null && default(T) is null);

        private static T ConvertObject(object? value)
        {
            if (value is T item)
            {
                return item;
            }
            if (value is null && default(T) is null)
            {
                return default!;
            }
            throw new System.ArgumentException();
        }
    }
}
