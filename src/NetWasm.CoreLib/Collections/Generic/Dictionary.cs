// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib Dictionary.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    public class Dictionary<TKey, TValue> :
        IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    {
        private int[] _buckets;
        private Entry[] _entries;
        private int _count;
        private int _version;
        private readonly IEqualityComparer<TKey> _comparer;
        private KeyCollection? _keys;
        private ValueCollection? _values;

        public Dictionary() : this(0, null)
        {
        }

        public Dictionary(int capacity) : this(capacity, null)
        {
        }

        public Dictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer)
        {
        }

        public Dictionary(int capacity, IEqualityComparer<TKey>? comparer)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            var size = capacity == 0 ? 0 : GetCapacity(capacity);
            _buckets = new int[size];
            _entries = new Entry[size];
        }

        public Dictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null)
        {
        }

        public Dictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer) :
            this(dictionary == null ? throw new System.ArgumentNullException() : dictionary.Count, comparer)
        {
            foreach (var pair in dictionary)
            {
                Add(pair.Key, pair.Value);
            }
        }

        public Dictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : this(collection, null)
        {
        }

        public Dictionary(
            IEnumerable<KeyValuePair<TKey, TValue>> collection,
            IEqualityComparer<TKey>? comparer) :
            this(collection == null ? throw new System.ArgumentNullException() : 0, comparer)
        {
            if (collection is ICollection<KeyValuePair<TKey, TValue>> sized)
            {
                EnsureCapacity(sized.Count);
            }
            foreach (var pair in collection)
            {
                Add(pair.Key, pair.Value);
            }
        }

        public int Count
        {
            get => _count;
        }

        public int Capacity
        {
            get => _entries.Length;
        }

        public bool IsReadOnly => false;
        public IEqualityComparer<TKey> Comparer
        {
            get => _comparer;
        }

        public KeyCollection Keys
        {
            get => _keys ??= new KeyCollection(this);
        }

        public ValueCollection Values
        {
            get => _values ??= new ValueCollection(this);
        }
        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public TValue this[TKey key]
        {
            get
            {
                var index = FindIndex(key);
                if (index < 0)
                {
                    throw new System.Collections.Generic.KeyNotFoundException();
                }
                return _entries[index].Value;
            }
            set => Insert(key, value, overwrite: true, throwOnExisting: false);
        }

        public void Add(TKey key, TValue value) =>
            Insert(key, value, overwrite: false, throwOnExisting: true);

        public bool TryAdd(TKey key, TValue value) =>
            Insert(key, value, overwrite: false, throwOnExisting: false);

        public bool ContainsKey(TKey key) => FindIndex(key) >= 0;

        public bool ContainsValue(TValue value)
        {
            var comparer = EqualityComparer<TValue>.Default;
            for (var index = 0; index < _entries.Length; index++)
            {
                if (_entries[index].Occupied && comparer.Equals(_entries[index].Value, value))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var index = FindIndex(key);
            if (index >= 0)
            {
                value = _entries[index].Value;
                return true;
            }
            value = default!;
            return false;
        }

        internal bool TryGetKey(TKey key, out TKey actualKey)
        {
            var index = FindIndex(key);
            if (index >= 0)
            {
                actualKey = _entries[index].Key;
                return true;
            }
            actualKey = default!;
            return false;
        }

        internal ref TValue? GetValueRefOrAddDefault(TKey key, out bool exists)
        {
            var index = FindIndex(key);
            if (index >= 0)
            {
                exists = true;
                return ref System.Runtime.CompilerServices.Unsafe.As<TValue, TValue?>(ref _entries[index].Value);
            }
            Insert(key, default!, overwrite: false, throwOnExisting: false);
            exists = false;
            index = FindIndex(key);
            return ref System.Runtime.CompilerServices.Unsafe.As<TValue, TValue?>(ref _entries[index].Value);
        }

        internal ref TValue GetValueRefOrNullRef(TKey key)
        {
            var index = FindIndex(key);
            return ref (index >= 0
                ? ref _entries[index].Value
                : ref System.Runtime.CompilerServices.Unsafe.NullRef<TValue>());
        }

        public bool Remove(TKey key) => Remove(key, out _);

        public bool Remove(TKey key, out TValue value)
        {
            ValidateKey(key);
            var index = FindIndex(key, out var bucket, out var previous);
            if (index < 0)
            {
                value = default!;
                return false;
            }

            var entry = _entries[index];
            if (previous < 0)
            {
                _buckets[bucket] = entry.Next + 1;
            }
            else
            {
                _entries[previous].Next = entry.Next;
            }
            _entries[index] = default;
            _count--;
            value = entry.Value;
            return true;
        }

        public void Clear()
        {
            if (_count == 0)
            {
                return;
            }
            Array.Clear(_buckets);
            Array.Clear(_entries);
            _count = 0;
        }

        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (capacity <= _entries.Length)
            {
                return _entries.Length;
            }
            Resize(GetCapacity(capacity));
            return _entries.Length;
        }

        public void TrimExcess() => TrimExcess(_count);

        public void TrimExcess(int capacity)
        {
            if (capacity < _count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (capacity >= _entries.Length)
            {
                return;
            }
            Resize(capacity == 0 ? 0 : GetCapacity(capacity));
        }

        public virtual void OnDeserialization(object? sender)
        {
        }

        public AlternateLookup<TAlternateKey> GetAlternateLookup<TAlternateKey>()
            where TAlternateKey : notnull, allows ref struct
        {
            if (!AlternateLookup<TAlternateKey>.IsCompatible(this))
            {
                throw new System.InvalidOperationException();
            }
            return new AlternateLookup<TAlternateKey>(this);
        }

        public bool TryGetAlternateLookup<TAlternateKey>(out AlternateLookup<TAlternateKey> lookup)
            where TAlternateKey : notnull, allows ref struct
        {
            if (AlternateLookup<TAlternateKey>.IsCompatible(this))
            {
                lookup = new AlternateLookup<TAlternateKey>(this);
                return true;
            }
            lookup = default;
            return false;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item) =>
            TryGetValue(item.Key, out var value) &&
            EqualityComparer<TValue>.Default.Equals(value, item.Value);

        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        public bool Remove(KeyValuePair<TKey, TValue> item) =>
            Contains(item) && Remove(item.Key);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ValidateCopy(array, arrayIndex, _count);
            for (var index = 0; index < _entries.Length; index++)
            {
                var entry = _entries[index];
                if (entry.Occupied)
                {
                    array[arrayIndex++] = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                }
            }
        }

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
            GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        internal int FindIndex(TKey key, out int bucket, out int previous)
        {
            ValidateKey(key);
            if (_buckets.Length == 0)
            {
                bucket = -1;
                previous = -1;
                return -1;
            }

            var hash = PositiveHash(key);
            bucket = hash % _buckets.Length;
            previous = -1;
            var current = _buckets[bucket] - 1;
            for (var collisions = 0; collisions <= _entries.Length && current >= 0; collisions++)
            {
                var entry = _entries[current];
                if (entry.Occupied && entry.HashCode == hash && _comparer.Equals(entry.Key, key))
                {
                    return current;
                }
                previous = current;
                current = entry.Next;
            }
            if (current >= 0)
            {
                throw new System.InvalidOperationException();
            }
            return -1;
        }

        private int FindIndex(TKey key)
        {
            var index = FindIndex(key, out _, out _);
            return index;
        }

        private bool Insert(TKey key, TValue value, bool overwrite, bool throwOnExisting)
        {
            ValidateKey(key);
            if (_buckets.Length == 0 || _count == _entries.Length)
            {
                Resize(GetCapacity(_count + 1));
            }
            var hash = PositiveHash(key);
            var bucket = hash % _buckets.Length;
            var current = _buckets[bucket] - 1;
            for (var collisions = 0; collisions <= _entries.Length && current >= 0; collisions++)
            {
                var entry = _entries[current];
                if (entry.HashCode == hash && _comparer.Equals(entry.Key, key))
                {
                    if (overwrite)
                    {
                        _entries[current].Value = value;
                        return true;
                    }
                    if (throwOnExisting)
                    {
                        throw new System.ArgumentException();
                    }
                    return false;
                }
                current = entry.Next;
            }
            if (current >= 0)
            {
                throw new System.InvalidOperationException();
            }

            var index = FindFreeIndex();
            _entries[index] = new Entry(hash, _buckets[bucket] - 1, key, value);
            _buckets[bucket] = index + 1;
            _count++;
            _version++;
            return true;
        }

        private int FindFreeIndex()
        {
            for (var index = 0; index < _entries.Length; index++)
            {
                if (!_entries[index].Occupied)
                {
                    return index;
                }
            }
            throw new System.InvalidOperationException();
        }

        private void Resize(int size)
        {
            if (size == 0)
            {
                _buckets = new int[0];
                _entries = new Entry[0];
                return;
            }
            var buckets = new int[size];
            var entries = new Entry[size];
            var destination = 0;
            for (var index = 0; index < _entries.Length; index++)
            {
                var entry = _entries[index];
                if (entry.Occupied)
                {
                    var bucket = entry.HashCode % size;
                    entry.Next = buckets[bucket] - 1;
                    entries[destination] = entry;
                    buckets[bucket] = destination + 1;
                    destination++;
                }
            }
            _buckets = buckets;
            _entries = entries;
        }

        private int PositiveHash(TKey key) => _comparer.GetHashCode(key) & 0x7fffffff;

        private static int GetCapacity(int required)
        {
            var capacity = 4;
            while (capacity < required)
            {
                capacity = checked(capacity * 2);
            }
            return capacity;
        }

        private static void ValidateKey(TKey key)
        {
            if (key is null)
            {
                throw new System.ArgumentNullException();
            }
        }

        private static void ValidateCopy<TItem>(TItem[] array, int index, int count)
        {
            if (array == null)
            {
                throw new System.ArgumentNullException();
            }
            if (index < 0 || index > array.Length)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            if (count > array.Length - index)
            {
                throw new System.ArgumentException();
            }
        }

        private struct Entry
        {
            internal bool Occupied;
            internal readonly int HashCode;
            internal int Next;
            internal readonly TKey Key;
            internal TValue Value;

            internal Entry(int hashCode, int next, TKey key, TValue value)
            {
                Occupied = true;
                HashCode = hashCode;
                Next = next;
                Key = key;
                Value = value;
            }
        }

        public readonly struct AlternateLookup<TAlternateKey>
            where TAlternateKey : notnull, allows ref struct
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            private readonly IAlternateEqualityComparer<TAlternateKey, TKey> _comparer;

            internal AlternateLookup(Dictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _comparer = dictionary._comparer as IAlternateEqualityComparer<TAlternateKey, TKey>
                    ?? throw new System.InvalidOperationException();
            }

            internal static bool IsCompatible(Dictionary<TKey, TValue> dictionary) =>
                dictionary._comparer is IAlternateEqualityComparer<TAlternateKey, TKey>;

            public Dictionary<TKey, TValue> Dictionary
            {
                get => _dictionary;
            }

            public TValue this[TAlternateKey key]
            {
                get
                {
                    if (!TryGetValue(key, out var value))
                    {
                        throw new System.Collections.Generic.KeyNotFoundException();
                    }
                    return value;
                }
                set
                {
                    var primary = _comparer.Create(key);
                    _dictionary.Insert(primary, value, overwrite: true, throwOnExisting: false);
                }
            }

            public bool ContainsKey(TAlternateKey key) => FindIndex(key) >= 0;

            public bool TryAdd(TAlternateKey key, TValue value) =>
                _dictionary.Insert(_comparer.Create(key), value, overwrite: false, throwOnExisting: false);

            public bool TryGetValue(TAlternateKey key, out TValue value)
            {
                return TryGetValue(key, out _, out value);
            }

            public bool TryGetValue(TAlternateKey key, out TKey actualKey, out TValue value)
            {
                var index = FindIndex(key);
                if (index >= 0)
                {
                    actualKey = _dictionary._entries[index].Key;
                    value = _dictionary._entries[index].Value;
                    return true;
                }
                actualKey = default!;
                value = default!;
                return false;
            }

            internal ref TValue? GetValueRefOrAddDefault(TAlternateKey key, out bool exists)
            {
                var index = FindIndex(key);
                if (index >= 0)
                {
                    exists = true;
                    return ref System.Runtime.CompilerServices.Unsafe.As<TValue, TValue?>(ref _dictionary._entries[index].Value);
                }
                var primary = _comparer.Create(key);
                return ref _dictionary.GetValueRefOrAddDefault(primary, out exists);
            }

            internal ref TValue GetValueRefOrNullRef(TAlternateKey key)
            {
                var index = FindIndex(key);
                return ref (index >= 0
                    ? ref _dictionary._entries[index].Value
                    : ref System.Runtime.CompilerServices.Unsafe.NullRef<TValue>());
            }

            public bool Remove(TAlternateKey key) => Remove(key, out _, out _);

            public bool Remove(TAlternateKey key, out TKey actualKey, out TValue value)
            {
                var index = FindIndex(key);
                if (index < 0)
                {
                    actualKey = default!;
                    value = default!;
                    return false;
                }
                actualKey = _dictionary._entries[index].Key;
                return _dictionary.Remove(actualKey, out value);
            }

            private int FindIndex(TAlternateKey key)
            {
                if (_dictionary._buckets.Length == 0)
                {
                    return -1;
                }
                var hash = _comparer.GetHashCode(key) & 0x7fffffff;
                var current = _dictionary._buckets[hash % _dictionary._buckets.Length] - 1;
                for (var collisions = 0; collisions <= _dictionary._entries.Length && current >= 0; collisions++)
                {
                    var entry = _dictionary._entries[current];
                    if (entry.Occupied && entry.HashCode == hash && _comparer.Equals(key, entry.Key))
                    {
                        return current;
                    }
                    current = entry.Next;
                }
                if (current >= 0)
                {
                    throw new System.InvalidOperationException();
                }
                return -1;
            }
        }

        public struct Enumerator :
            IEnumerator<KeyValuePair<TKey, TValue>>,
            System.Collections.IDictionaryEnumerator,
            System.Collections.IEnumerator,
            System.IDisposable
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(Dictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _current = default;
            }

            public KeyValuePair<TKey, TValue> Current
            {
                get => _current;
            }

            public System.Collections.DictionaryEntry Entry =>
                new(_current.Key!, _current.Value);
            public object Key => _current.Key!;
            public object? Value => _current.Value;
            object System.Collections.IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                {
                    throw new System.InvalidOperationException();
                }
                while (_index < _dictionary._entries.Length)
                {
                    var entry = _dictionary._entries[_index++];
                    if (entry.Occupied)
                    {
                        _current = new KeyValuePair<TKey, TValue>(entry.Key, entry.Value);
                        return true;
                    }
                }
                _current = default;
                return false;
            }

            public void Dispose()
            {
            }

            void System.Collections.IEnumerator.Reset() => throw new System.NotSupportedException();
        }

        public sealed class KeyCollection :
            ICollection<TKey>,
            IEnumerable<TKey>,
            IReadOnlyCollection<TKey>,
            System.Collections.ICollection,
            System.Collections.IEnumerable
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            public KeyCollection(Dictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    throw new System.ArgumentNullException();
                }
                _dictionary = dictionary;
            }
            public int Count
            {
                get => _dictionary.Count;
            }
            bool ICollection<TKey>.IsReadOnly => true;
            public bool Contains(TKey item) => _dictionary.ContainsKey(item);
            public void CopyTo(TKey[] array, int arrayIndex)
            {
                ValidateCopy(array, arrayIndex, Count);
                foreach (var pair in _dictionary) array[arrayIndex++] = pair.Key;
            }
            public Enumerator GetEnumerator() => new(_dictionary);
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<TKey>.Add(TKey item) => throw new System.NotSupportedException();
            void ICollection<TKey>.Clear() => throw new System.NotSupportedException();
            bool ICollection<TKey>.Remove(TKey item) => throw new System.NotSupportedException();

            bool System.Collections.ICollection.IsSynchronized => false;
            object System.Collections.ICollection.SyncRoot => this;
            void System.Collections.ICollection.CopyTo(Array array, int arrayIndex)
            {
                if (array is TKey[] typed)
                {
                    CopyTo(typed, arrayIndex);
                    return;
                }
                throw new System.ArgumentException();
            }

            public struct Enumerator : IEnumerator<TKey>, System.Collections.IEnumerator, System.IDisposable
            {
                private Dictionary<TKey, TValue>.Enumerator _inner;
                internal Enumerator(Dictionary<TKey, TValue> dictionary) => _inner = new(dictionary);
                public TKey Current
                {
                    get => _inner.Current.Key;
                }
                object System.Collections.IEnumerator.Current => Current!;
                public bool MoveNext() => _inner.MoveNext();
                public void Dispose() => _inner.Dispose();
                void System.Collections.IEnumerator.Reset() => throw new System.NotSupportedException();
            }
        }

        public sealed class ValueCollection :
            ICollection<TValue>,
            IEnumerable<TValue>,
            IReadOnlyCollection<TValue>,
            System.Collections.ICollection,
            System.Collections.IEnumerable
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            public ValueCollection(Dictionary<TKey, TValue> dictionary)
            {
                if (dictionary == null)
                {
                    throw new System.ArgumentNullException();
                }
                _dictionary = dictionary;
            }
            public int Count
            {
                get => _dictionary.Count;
            }
            bool ICollection<TValue>.IsReadOnly => true;
            public bool Contains(TValue item)
            {
                var comparer = EqualityComparer<TValue>.Default;
                foreach (var pair in _dictionary)
                    if (comparer.Equals(pair.Value, item)) return true;
                return false;
            }
            public void CopyTo(TValue[] array, int arrayIndex)
            {
                ValidateCopy(array, arrayIndex, Count);
                foreach (var pair in _dictionary) array[arrayIndex++] = pair.Value;
            }
            public Enumerator GetEnumerator() => new(_dictionary);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<TValue>.Add(TValue item) => throw new System.NotSupportedException();
            void ICollection<TValue>.Clear() => throw new System.NotSupportedException();
            bool ICollection<TValue>.Remove(TValue item) => throw new System.NotSupportedException();

            bool System.Collections.ICollection.IsSynchronized => false;
            object System.Collections.ICollection.SyncRoot => this;
            void System.Collections.ICollection.CopyTo(Array array, int arrayIndex)
            {
                if (array is TValue[] typed)
                {
                    CopyTo(typed, arrayIndex);
                    return;
                }
                throw new System.ArgumentException();
            }

            public struct Enumerator : IEnumerator<TValue>, System.Collections.IEnumerator, System.IDisposable
            {
                private Dictionary<TKey, TValue>.Enumerator _inner;
                internal Enumerator(Dictionary<TKey, TValue> dictionary) => _inner = new(dictionary);
                public TValue Current
                {
                    get => _inner.Current.Value;
                }
                object System.Collections.IEnumerator.Current => Current!;
                public bool MoveNext() => _inner.MoveNext();
                public void Dispose() => _inner.Dispose();
                void System.Collections.IEnumerator.Reset() => throw new System.NotSupportedException();
            }
        }

        // Compatibility aliases retained for existing NetWasm callers.
        public struct KeyEnumerator : IEnumerator<TKey>
        {
            private KeyCollection.Enumerator _inner;
            internal KeyEnumerator(Dictionary<TKey, TValue> dictionary) => _inner = new(dictionary);
            public TKey Current => _inner.Current;
            object System.Collections.IEnumerator.Current => Current!;
            public bool MoveNext() => _inner.MoveNext();
            public void Dispose() => _inner.Dispose();
            void System.Collections.IEnumerator.Reset() => throw new System.NotSupportedException();
        }

        public struct ValueEnumerator : IEnumerator<TValue>
        {
            private ValueCollection.Enumerator _inner;
            internal ValueEnumerator(Dictionary<TKey, TValue> dictionary) => _inner = new(dictionary);
            public TValue Current => _inner.Current;
            object System.Collections.IEnumerator.Current => Current!;
            public bool MoveNext() => _inner.MoveNext();
            public void Dispose() => _inner.Dispose();
            void System.Collections.IEnumerator.Reset() => throw new System.NotSupportedException();
        }
    }
}
