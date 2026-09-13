// Portions derived from dotnet/runtime System.Private.CoreLib ArrayList at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections
{
    public class ArrayList : ICollection, IEnumerable, IList, ICloneable
    {
        private object?[] _items;
        private int _size;
        private int _version;
        private const int DefaultCapacity = 4;

        public ArrayList() => _items = new object?[0];

        public ArrayList(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new object?[capacity];
        }

        public ArrayList(ICollection collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            _items = new object?[collection.Count];
            AddRange(collection);
        }

        public virtual int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == _items.Length) return;
                var replacement = new object?[value];
                Array.Copy(_items, 0, replacement, 0, _size);
                _items = replacement;
            }
        }

        public virtual int Count
        {
            get => _size;
        }

        public virtual bool IsFixedSize
        {
            get => false;
        }

        public virtual bool IsReadOnly
        {
            get => false;
        }

        public virtual bool IsSynchronized
        {
            get => false;
        }

        public virtual object SyncRoot
        {
            get => this;
        }

        public virtual object? this[int index]
        {
            get { ValidateIndex(index); return _items[index]; }
            set { ValidateIndex(index); _items[index] = value; _version++; }
        }

        public static ArrayList Adapter(IList list)
        {
            ArgumentNullException.ThrowIfNull(list);
            return new IListWrapper(list);
        }

        public virtual int Add(object? value)
        {
            EnsureCapacity(_size + 1);
            _items[_size] = value;
            _version++;
            return _size++;
        }

        public virtual void AddRange(ICollection collection) => InsertRange(_size, collection);

        public virtual int BinarySearch(object? value) => BinarySearch(0, _size, value, null);
        public virtual int BinarySearch(object? value, IComparer? comparer) => BinarySearch(0, _size, value, comparer);
        public virtual int BinarySearch(int index, int count, object? value, IComparer? comparer)
        {
            ValidateRange(index, count);
            comparer ??= ComparerAdapter.Instance;
            var low = index;
            var high = index + count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                var comparison = comparer.Compare(_items[middle], value);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }
            return ~low;
        }

        public virtual void Clear()
        {
            for (var index = 0; index < _size; index++) _items[index] = null;
            _size = 0;
            _version++;
        }

        public virtual object Clone()
        {
            var clone = new ArrayList(_size);
            Array.Copy(_items, 0, clone._items, 0, _size);
            clone._size = _size;
            return clone;
        }

        public virtual bool Contains(object? value) => IndexOf(value) >= 0;

        public virtual void CopyTo(Array array) => CopyTo(0, array, 0, _size);
        public virtual void CopyTo(Array array, int index) => CopyTo(0, array, index, _size);
        public virtual void CopyTo(int sourceIndex, Array array, int destinationIndex, int count)
        {
            ArgumentNullException.ThrowIfNull(array);
            ValidateRange(sourceIndex, count);
            if (destinationIndex < 0 || destinationIndex > array.Length - count)
                throw new ArgumentException();
            if (array is not object[] destination) throw new ArgumentException();
            Array.Copy(_items, sourceIndex, destination, destinationIndex, count);
        }

        public virtual IEnumerator GetEnumerator() => new ArrayListEnumerator(this, 0, _size);
        public virtual IEnumerator GetEnumerator(int index, int count)
        {
            ValidateRange(index, count);
            return new ArrayListEnumerator(this, index, count);
        }

        public virtual ArrayList GetRange(int index, int count)
        {
            ValidateRange(index, count);
            var result = new ArrayList(count);
            Array.Copy(_items, index, result._items, 0, count);
            result._size = count;
            return result;
        }

        public virtual int IndexOf(object? value) => IndexOf(value, 0, _size);
        public virtual int IndexOf(object? value, int startIndex) => IndexOf(value, startIndex, _size - startIndex);
        public virtual int IndexOf(object? value, int startIndex, int count)
        {
            ValidateRange(startIndex, count);
            for (var index = startIndex; index < startIndex + count; index++)
                if (object.Equals(_items[index], value)) return index;
            return -1;
        }

        public virtual void Insert(int index, object? value)
        {
            if ((uint)index > (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            EnsureCapacity(_size + 1);
            for (var current = _size; current > index; current--) _items[current] = _items[current - 1];
            _items[index] = value;
            _size++;
            _version++;
        }

        public virtual void InsertRange(int index, ICollection collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            if ((uint)index > (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            if (collection.Count == 0) return;
            var values = new object?[collection.Count];
            collection.CopyTo(values, 0);
            EnsureCapacity(_size + values.Length);
            for (var current = _size - 1; current >= index; current--) _items[current + values.Length] = _items[current];
            Array.Copy(values, 0, _items, index, values.Length);
            _size += values.Length;
            _version++;
        }

        public virtual int LastIndexOf(object? value) => LastIndexOf(value, _size - 1, _size);
        public virtual int LastIndexOf(object? value, int startIndex) => LastIndexOf(value, startIndex, startIndex + 1);
        public virtual int LastIndexOf(object? value, int startIndex, int count)
        {
            if (_size == 0) return -1;
            if ((uint)startIndex >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (count < 0 || count > startIndex + 1) throw new ArgumentOutOfRangeException(nameof(count));
            for (var index = startIndex; index > startIndex - count; index--)
                if (object.Equals(_items[index], value)) return index;
            return -1;
        }

        public virtual void Remove(object? value)
        {
            var index = IndexOf(value);
            if (index >= 0) RemoveAt(index);
        }

        public virtual void RemoveAt(int index)
        {
            ValidateIndex(index);
            _size--;
            for (var current = index; current < _size; current++) _items[current] = _items[current + 1];
            _items[_size] = null;
            _version++;
        }

        public virtual void RemoveRange(int index, int count)
        {
            ValidateRange(index, count);
            if (count == 0) return;
            var newSize = _size - count;
            for (var current = index; current < newSize; current++) _items[current] = _items[current + count];
            for (var current = newSize; current < _size; current++) _items[current] = null;
            _size = newSize;
            _version++;
        }

        public virtual void Reverse() => Reverse(0, _size);
        public virtual void Reverse(int index, int count)
        {
            ValidateRange(index, count);
            var end = index + count - 1;
            while (index < end) { (_items[index], _items[end]) = (_items[end], _items[index]); index++; end--; }
            if (count > 1) _version++;
        }

        public virtual void SetRange(int index, ICollection collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            ValidateRange(index, collection.Count);
            var values = new object?[collection.Count];
            collection.CopyTo(values, 0);
            Array.Copy(values, 0, _items, index, values.Length);
            _version++;
        }

        public virtual void Sort() => Sort(0, _size, null);
        public virtual void Sort(IComparer? comparer) => Sort(0, _size, comparer);
        public virtual void Sort(int index, int count, IComparer? comparer)
        {
            ValidateRange(index, count);
            comparer ??= ComparerAdapter.Instance;
            for (var current = index + 1; current < index + count; current++)
            {
                var value = _items[current];
                var position = current;
                while (position > index && comparer.Compare(_items[position - 1], value) > 0)
                { _items[position] = _items[position - 1]; position--; }
                _items[position] = value;
            }
            if (count > 1) _version++;
        }

        public virtual object[] ToArray()
        {
            var result = new object[_size];
            Array.Copy(_items, 0, result, 0, _size);
            return result;
        }

        public virtual Array ToArray(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type == typeof(object)) return ToArray();
            throw new PlatformNotSupportedException();
        }

        public virtual void TrimToSize() => Capacity = _size;

        public static ArrayList FixedSize(ArrayList list) { ArgumentNullException.ThrowIfNull(list); return new FixedSizeList(list); }
        public static IList FixedSize(IList list) { ArgumentNullException.ThrowIfNull(list); return new FixedSizeList(list); }
        public static ArrayList ReadOnly(ArrayList list) { ArgumentNullException.ThrowIfNull(list); return new ReadOnlyList(list); }
        public static IList ReadOnly(IList list) { ArgumentNullException.ThrowIfNull(list); return new ReadOnlyList(list); }
        public static ArrayList Synchronized(ArrayList list) { ArgumentNullException.ThrowIfNull(list); return new SynchronizedList(list); }
        public static IList Synchronized(IList list) { ArgumentNullException.ThrowIfNull(list); return new SynchronizedList(list); }

        public static ArrayList Repeat(object? value, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var result = new ArrayList(count);
            for (var index = 0; index < count; index++) result.Add(value);
            return result;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _items.Length) return;
            var capacity = _items.Length == 0 ? DefaultCapacity : _items.Length * 2;
            if (capacity < required) capacity = required;
            Capacity = capacity;
        }

        private void ValidateIndex(int index)
        { if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index)); }
        private void ValidateRange(int index, int count)
        { if (index < 0 || count < 0 || index > _size - count) throw new ArgumentOutOfRangeException(); }

        private sealed class ArrayListEnumerator : IEnumerator
        {
            private readonly ArrayList _owner; private readonly int _start; private readonly int _end; private int _index; private readonly int _version;
            public ArrayListEnumerator(ArrayList owner, int start, int count) { _owner = owner; _start = start; _end = start + count; _index = start - 1; _version = owner._version; }
            public object Current { get { if (_index < _start || _index >= _end) throw new InvalidOperationException(); return _owner._items[_index]!; } }
            public bool MoveNext() { if (_version != _owner._version) throw new InvalidOperationException(); if (_index < _end) _index++; return _index < _end; }
            public void Reset() { if (_version != _owner._version) throw new InvalidOperationException(); _index = _start - 1; }
        }

        private sealed class ComparerAdapter : IComparer
        {
            internal static readonly ComparerAdapter Instance = new();
            public int Compare(object? x, object? y)
            { if (ReferenceEquals(x, y)) return 0; if (x is null) return -1; if (y is null) return 1; if (x is IComparable comparable) return comparable.CompareTo(y); throw new ArgumentException(); }
        }

        private class IListWrapper : ArrayList
        {
            private readonly IList _list;
            public IListWrapper(IList list) => _list = list;
            public override int Capacity { get => _list.Count; set => throw new NotSupportedException(); }
            public override int Count => _list.Count;
            public override bool IsFixedSize => _list.IsFixedSize;
            public override bool IsReadOnly => _list.IsReadOnly;
            public override bool IsSynchronized => _list.IsSynchronized;
            public override object SyncRoot => _list.SyncRoot;
            public override object? this[int index] { get => _list[index]; set => _list[index] = value; }
            public override int Add(object? value) => _list.Add(value);
            public override void AddRange(ICollection collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                foreach (var value in collection) _list.Add(value);
            }
            public override int BinarySearch(object? value) => BinarySearch(0, Count, value, null);
            public override int BinarySearch(object? value, IComparer? comparer) => BinarySearch(0, Count, value, comparer);
            public override int BinarySearch(int index, int count, object? value, IComparer? comparer)
            {
                ValidateWrapperRange(index, count);
                comparer ??= ComparerAdapter.Instance;
                var low = index;
                var high = index + count - 1;
                while (low <= high)
                {
                    var middle = low + ((high - low) >> 1);
                    var comparison = comparer.Compare(_list[middle], value);
                    if (comparison == 0) return middle;
                    if (comparison < 0) low = middle + 1;
                    else high = middle - 1;
                }
                return ~low;
            }
            public override void Clear() => _list.Clear();
            public override bool Contains(object? value) => _list.Contains(value);
            public override void CopyTo(Array array) => _list.CopyTo(array, 0);
            public override void CopyTo(Array array, int index) => _list.CopyTo(array, index);
            public override void CopyTo(int sourceIndex, Array array, int destinationIndex, int count)
            {
                ValidateWrapperRange(sourceIndex, count);
                if (array is not object[] destination || destinationIndex < 0 || destinationIndex > destination.Length - count) throw new ArgumentException();
                for (var offset = 0; offset < count; offset++) destination[destinationIndex + offset] = _list[sourceIndex + offset]!;
            }
            public override IEnumerator GetEnumerator() => _list.GetEnumerator();
            public override IEnumerator GetEnumerator(int index, int count)
            {
                ValidateWrapperRange(index, count);
                var result = new ArrayList(count);
                for (var offset = 0; offset < count; offset++) result.Add(_list[index + offset]);
                return result.GetEnumerator();
            }
            public override ArrayList GetRange(int index, int count)
            {
                ValidateWrapperRange(index, count);
                var result = new ArrayList(count);
                for (var offset = 0; offset < count; offset++) result.Add(_list[index + offset]);
                return result;
            }
            public override int IndexOf(object? value) => _list.IndexOf(value);
            public override int IndexOf(object? value, int startIndex) => IndexOf(value, startIndex, Count - startIndex);
            public override int IndexOf(object? value, int startIndex, int count)
            {
                ValidateWrapperRange(startIndex, count);
                for (var index = startIndex; index < startIndex + count; index++) if (object.Equals(_list[index], value)) return index;
                return -1;
            }
            public override void Insert(int index, object? value) => _list.Insert(index, value);
            public override void InsertRange(int index, ICollection collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                var values = new object?[collection.Count];
                collection.CopyTo(values, 0);
                for (var offset = 0; offset < values.Length; offset++) _list.Insert(index + offset, values[offset]);
            }
            public override int LastIndexOf(object? value) => LastIndexOf(value, Count - 1, Count);
            public override int LastIndexOf(object? value, int startIndex) => LastIndexOf(value, startIndex, startIndex + 1);
            public override int LastIndexOf(object? value, int startIndex, int count)
            {
                if (startIndex < 0 || count < 0 || startIndex >= Count || count > startIndex + 1) throw new ArgumentOutOfRangeException();
                for (var index = startIndex; index >= startIndex - count + 1; index--) if (object.Equals(this[index], value)) return index;
                return -1;
            }
            public override void Remove(object? value) => _list.Remove(value);
            public override void RemoveAt(int index) => _list.RemoveAt(index);
            public override void RemoveRange(int index, int count)
            {
                ValidateWrapperRange(index, count);
                for (var offset = 0; offset < count; offset++) _list.RemoveAt(index);
            }
            public override void Reverse() => Reverse(0, Count);
            public override void Reverse(int index, int count)
            {
                ValidateWrapperRange(index, count);
                var end = index + count - 1;
                while (index < end) { var value = _list[index]; _list[index] = _list[end]; _list[end] = value; index++; end--; }
            }
            public override void SetRange(int index, ICollection collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                ValidateWrapperRange(index, collection.Count);
                var values = new object?[collection.Count];
                collection.CopyTo(values, 0);
                for (var offset = 0; offset < values.Length; offset++) _list[index + offset] = values[offset];
            }
            public override void Sort() => Sort(0, Count, null);
            public override void Sort(IComparer? comparer) => Sort(0, Count, comparer);
            public override void Sort(int index, int count, IComparer? comparer)
            {
                ValidateWrapperRange(index, count);
                for (var current = index + 1; current < index + count; current++)
                {
                    var value = _list[current];
                    var position = current;
                    while (position > index && (comparer ?? ComparerAdapter.Instance).Compare(_list[position - 1], value) > 0)
                    { _list[position] = _list[position - 1]; position--; }
                    _list[position] = value;
                }
            }
            public override object[] ToArray()
            {
                var result = new object[Count];
                _list.CopyTo(result, 0);
                return result;
            }
            public override Array ToArray(Type type)
            {
                ArgumentNullException.ThrowIfNull(type);
                if (type == typeof(object)) return ToArray();
                throw new PlatformNotSupportedException();
            }
            public override void TrimToSize() { }

            private void ValidateWrapperRange(int index, int count)
            { if (index < 0 || count < 0 || index > Count - count) throw new ArgumentOutOfRangeException(); }
        }

        private sealed class FixedSizeList : IListWrapper
        {
            public FixedSizeList(IList list) : base(list) { }
            public override bool IsFixedSize => true;
            public override int Add(object? value) => throw new NotSupportedException();
            public override void Clear() => throw new NotSupportedException();
            public override void Insert(int index, object? value) => throw new NotSupportedException();
            public override void Remove(object? value) => throw new NotSupportedException();
            public override void RemoveAt(int index) => throw new NotSupportedException();
        }

        private sealed class ReadOnlyList : IListWrapper
        {
            public ReadOnlyList(IList list) : base(list) { }
            public override bool IsReadOnly => true;
            public override object? this[int index] { get => base[index]; set => throw new NotSupportedException(); }
            public override int Add(object? value) => throw new NotSupportedException();
            public override void Clear() => throw new NotSupportedException();
            public override void Insert(int index, object? value) => throw new NotSupportedException();
            public override void Remove(object? value) => throw new NotSupportedException();
            public override void RemoveAt(int index) => throw new NotSupportedException();
        }

        private sealed class SynchronizedList : IListWrapper
        {
            public SynchronizedList(IList list) : base(list) { }
            public override bool IsSynchronized => true;
        }
    }
}
