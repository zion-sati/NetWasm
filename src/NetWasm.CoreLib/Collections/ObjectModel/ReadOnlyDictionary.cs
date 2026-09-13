// Portions derived from dotnet/runtime System.Private.CoreLib's
// ReadOnlyDictionary<TKey,TValue> at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements; the .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Collections.ObjectModel
{
    public class ReadOnlyDictionary<TKey, TValue> :
        Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>,
        Collections.Generic.IDictionary<TKey, TValue>,
        Collections.Generic.IEnumerable<Collections.Generic.KeyValuePair<TKey, TValue>>,
        Collections.Generic.IReadOnlyCollection<Collections.Generic.KeyValuePair<TKey, TValue>>,
        Collections.Generic.IReadOnlyDictionary<TKey, TValue>,
        Collections.ICollection, Collections.IDictionary, Collections.IEnumerable
        where TKey : notnull
    {
        private readonly Collections.Generic.IDictionary<TKey, TValue> _dictionary;
        private readonly KeyCollection _keys;
        private readonly ValueCollection _values;

        public ReadOnlyDictionary(Collections.Generic.IDictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException();
            _keys = new KeyCollection(this);
            _values = new ValueCollection(this);
        }

        public static ReadOnlyDictionary<TKey, TValue> Empty { get; } =
            new(new Collections.Generic.Dictionary<TKey, TValue>());

        protected Collections.Generic.IDictionary<TKey, TValue> Dictionary => _dictionary;
        public int Count { get => _dictionary.Count; }
        public TValue this[TKey key] { get => _dictionary[key]; }
        TValue Collections.Generic.IDictionary<TKey, TValue>.this[TKey key]
        {
            get => _dictionary[key];
            set => ThrowReadOnly();
        }

        public KeyCollection Keys { get => _keys; }
        public ValueCollection Values { get => _values; }

        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) =>
            _dictionary.TryGetValue(key, out value);

        Collections.Generic.IEnumerable<TKey>
            Collections.Generic.IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        Collections.Generic.IEnumerable<TValue>
            Collections.Generic.IReadOnlyDictionary<TKey, TValue>.Values => Values;

        Collections.Generic.ICollection<TKey>
            Collections.Generic.IDictionary<TKey, TValue>.Keys => Keys;
        Collections.Generic.ICollection<TValue>
            Collections.Generic.IDictionary<TKey, TValue>.Values => Values;

        public Collections.Generic.IEnumerator<Collections.Generic.KeyValuePair<TKey, TValue>>
            GetEnumerator() => _dictionary.GetEnumerator();
        Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        Collections.IDictionaryEnumerator Collections.IDictionary.GetEnumerator() =>
            new DictionaryEnumerator(this);

        bool Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>.IsReadOnly => true;
        void Collections.Generic.IDictionary<TKey, TValue>.Add(TKey key, TValue value) => ThrowReadOnly();
        bool Collections.Generic.IDictionary<TKey, TValue>.Remove(TKey key) => ThrowReadOnly<bool>();
        void Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>.Add(
            Collections.Generic.KeyValuePair<TKey, TValue> item) => ThrowReadOnly();
        void Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>.Clear() => ThrowReadOnly();
        bool Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>.Contains(
            Collections.Generic.KeyValuePair<TKey, TValue> item) =>
            _dictionary.TryGetValue(item.Key, out var value) &&
            Collections.Generic.EqualityComparer<TValue>.Default.Equals(value, item.Value);
        void Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>.CopyTo(
            Collections.Generic.KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException();
            }
            if (index < 0 || index > array.Length - Count)
            {
                throw new ArgumentOutOfRangeException();
            }
            foreach (var item in _dictionary)
            {
                array[index++] = item;
            }
        }
        bool Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>.Remove(
            Collections.Generic.KeyValuePair<TKey, TValue> item) => ThrowReadOnly<bool>();

        object? Collections.IDictionary.this[object key]
        {
            get => key is TKey typed && _dictionary.TryGetValue(typed, out var value) ? value : null;
            set => ThrowReadOnly();
        }
        Collections.ICollection Collections.IDictionary.Keys => new KeyCollectionView(this);
        Collections.ICollection Collections.IDictionary.Values => new ValueCollectionView(this);
        bool Collections.IDictionary.IsReadOnly => true;
        bool Collections.IDictionary.IsFixedSize => true;
        bool Collections.IDictionary.Contains(object key) =>
            key is TKey typed && _dictionary.ContainsKey(typed);
        void Collections.IDictionary.Add(object key, object? value) => ThrowReadOnly();
        void Collections.IDictionary.Clear() => ThrowReadOnly();
        void Collections.IDictionary.Remove(object key) => ThrowReadOnly();
        bool Collections.ICollection.IsSynchronized => false;
        object Collections.ICollection.SyncRoot => this;
        void Collections.ICollection.CopyTo(Array array, int index)
        {
            if (array is Collections.Generic.KeyValuePair<TKey, TValue>[] typed)
            {
                ((Collections.Generic.ICollection<Collections.Generic.KeyValuePair<TKey, TValue>>)this)
                    .CopyTo(typed, index);
                return;
            }
            throw new ArgumentException();
        }

        private static void ThrowReadOnly() => throw new NotSupportedException();
        private static TResult ThrowReadOnly<TResult>() => throw new NotSupportedException();

        public sealed class KeyCollection : Collections.Generic.ICollection<TKey>,
            Collections.Generic.IEnumerable<TKey>, Collections.Generic.IReadOnlyCollection<TKey>,
            Collections.ICollection, Collections.IEnumerable
        {
            private readonly ReadOnlyDictionary<TKey, TValue> _owner;
            internal KeyCollection(ReadOnlyDictionary<TKey, TValue> owner) => _owner = owner;
            public int Count { get => _owner.Count; }
            public bool Contains(TKey item) => _owner.ContainsKey(item);
            public void CopyTo(TKey[] array, int index)
            {
                if (array == null) throw new ArgumentNullException();
                if (index < 0 || index > array.Length - Count) throw new ArgumentOutOfRangeException();
                var offset = index;
                foreach (var item in _owner._dictionary.Keys) array[offset++] = item;
            }
            public Collections.Generic.IEnumerator<TKey> GetEnumerator() =>
                _owner._dictionary.Keys.GetEnumerator();
            Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            bool Collections.Generic.ICollection<TKey>.IsReadOnly => true;
            void Collections.Generic.ICollection<TKey>.Add(TKey item) => ThrowReadOnly();
            void Collections.Generic.ICollection<TKey>.Clear() => ThrowReadOnly();
            bool Collections.Generic.ICollection<TKey>.Remove(TKey item) => ThrowReadOnly<bool>();
            bool Collections.ICollection.IsSynchronized => false;
            object Collections.ICollection.SyncRoot => this;
            void Collections.ICollection.CopyTo(Array array, int index)
            {
                if (array is TKey[] typed) { CopyTo(typed, index); return; }
                throw new ArgumentException();
            }
        }

        public sealed class ValueCollection : Collections.Generic.ICollection<TValue>,
            Collections.Generic.IEnumerable<TValue>, Collections.Generic.IReadOnlyCollection<TValue>,
            Collections.ICollection, Collections.IEnumerable
        {
            private readonly ReadOnlyDictionary<TKey, TValue> _owner;
            internal ValueCollection(ReadOnlyDictionary<TKey, TValue> owner) => _owner = owner;
            public int Count { get => _owner.Count; }
            public bool Contains(TValue item)
            {
                foreach (var value in _owner._dictionary.Values)
                    if (Collections.Generic.EqualityComparer<TValue>.Default.Equals(value, item)) return true;
                return false;
            }
            public void CopyTo(TValue[] array, int index)
            {
                if (array == null) throw new ArgumentNullException();
                if (index < 0 || index > array.Length - Count) throw new ArgumentOutOfRangeException();
                var offset = index;
                foreach (var item in _owner._dictionary.Values) array[offset++] = item;
            }
            public Collections.Generic.IEnumerator<TValue> GetEnumerator() =>
                _owner._dictionary.Values.GetEnumerator();
            Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            bool Collections.Generic.ICollection<TValue>.IsReadOnly => true;
            void Collections.Generic.ICollection<TValue>.Add(TValue item) => ThrowReadOnly();
            void Collections.Generic.ICollection<TValue>.Clear() => ThrowReadOnly();
            bool Collections.Generic.ICollection<TValue>.Remove(TValue item) => ThrowReadOnly<bool>();
            bool Collections.ICollection.IsSynchronized => false;
            object Collections.ICollection.SyncRoot => this;
            void Collections.ICollection.CopyTo(Array array, int index)
            {
                if (array is TValue[] typed) { CopyTo(typed, index); return; }
                throw new ArgumentException();
            }
        }

        private sealed class KeyCollectionView(ReadOnlyDictionary<TKey, TValue> owner) : Collections.ICollection
        {
            public int Count => owner.Count;
            public bool IsSynchronized => false;
            public object SyncRoot => owner;
            public void CopyTo(Array array, int index) => owner.Keys.CopyTo((TKey[])array, index);
            public Collections.IEnumerator GetEnumerator() => owner.Keys.GetEnumerator();
        }

        private sealed class ValueCollectionView(ReadOnlyDictionary<TKey, TValue> owner) : Collections.ICollection
        {
            public int Count => owner.Count;
            public bool IsSynchronized => false;
            public object SyncRoot => owner;
            public void CopyTo(Array array, int index) => owner.Values.CopyTo((TValue[])array, index);
            public Collections.IEnumerator GetEnumerator() => owner.Values.GetEnumerator();
        }

        private sealed class DictionaryEnumerator(ReadOnlyDictionary<TKey, TValue> owner) :
            Collections.IDictionaryEnumerator
        {
            private Collections.Generic.IEnumerator<Collections.Generic.KeyValuePair<TKey, TValue>> _enumerator =
                owner._dictionary.GetEnumerator();
            public object Current => Entry;
            public Collections.DictionaryEntry Entry
            {
                get
                {
                    var pair = _enumerator.Current;
                    return new Collections.DictionaryEntry(pair.Key!, pair.Value);
                }
            }
            public object Key => Entry.Key;
            public object? Value => Entry.Value;
            public bool MoveNext() => _enumerator.MoveNext();
            public void Reset() => throw new NotSupportedException();
        }

    }
}
