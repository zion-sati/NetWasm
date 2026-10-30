// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections.ObjectModel
{
    public class ReadOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary,
        IReadOnlyDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly IDictionary<TKey, TValue> m_dictionary;
        private KeyCollection? _keys;
        private ValueCollection? _values;

        public ReadOnlyDictionary(IDictionary<TKey, TValue> dictionary)
        {
            ArgumentNullException.ThrowIfNull(dictionary);
            m_dictionary = dictionary;
        }

        public static ReadOnlyDictionary<TKey, TValue> Empty { get; } =
            new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>());

        protected IDictionary<TKey, TValue> Dictionary => m_dictionary;

        public KeyCollection Keys => _keys ??= new KeyCollection(m_dictionary.Keys);

        public ValueCollection Values => _values ??= new ValueCollection(m_dictionary.Values);

        public bool ContainsKey(TKey key) => m_dictionary.ContainsKey(key);

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            return m_dictionary.TryGetValue(key, out value!);
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        public TValue this[TKey key] => m_dictionary[key];

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        bool IDictionary<TKey, TValue>.Remove(TKey key)
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        TValue IDictionary<TKey, TValue>.this[TKey key]
        {
            get => m_dictionary[key];
            set => throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        public int Count => m_dictionary.Count;

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return m_dictionary.Contains(item);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(
            KeyValuePair<TKey, TValue>[] array,
            int arrayIndex)
        {
            m_dictionary.CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => true;

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Clear()
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return m_dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)m_dictionary).GetEnumerator();
        }

        private static bool IsCompatibleKey(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return key is TKey;
        }

        void IDictionary.Add(object key, object? value)
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        void IDictionary.Clear()
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        bool IDictionary.Contains(object key)
        {
            return IsCompatibleKey(key) && ContainsKey((TKey)key);
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            if (m_dictionary is IDictionary dictionary)
            {
                return dictionary.GetEnumerator();
            }
            return new DictionaryEnumerator(m_dictionary);
        }

        bool IDictionary.IsFixedSize => true;

        bool IDictionary.IsReadOnly => true;

        ICollection IDictionary.Keys => Keys;

        void IDictionary.Remove(object key)
        {
            throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        ICollection IDictionary.Values => Values;

        object? IDictionary.this[object key]
        {
            get
            {
                if (!IsCompatibleKey(key))
                {
                    return null;
                }

                if (m_dictionary.TryGetValue((TKey)key, out TValue? value))
                {
                    return value;
                }
                return null;
            }
            set => throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            CollectionHelpers.ValidateCopyToArguments(Count, array, index);

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                m_dictionary.CopyTo(pairs, index);
            }
            else if (array is DictionaryEntry[] entries)
            {
                foreach (KeyValuePair<TKey, TValue> item in m_dictionary)
                {
                    entries[index++] = new DictionaryEntry(item.Key, item.Value);
                }
            }
            else
            {
                object[] objects = array as object[] ??
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                try
                {
                    foreach (KeyValuePair<TKey, TValue> item in m_dictionary)
                    {
                        objects[index++] = new KeyValuePair<TKey, TValue>(item.Key, item.Value);
                    }
                }
                catch (ArrayTypeMismatchException)
                {
                    throw new ArgumentException(SR.Argument_IncompatibleArrayType, nameof(array));
                }
            }
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => m_dictionary is ICollection collection
            ? collection.SyncRoot
            : this;

        private readonly struct DictionaryEnumerator : IDictionaryEnumerator
        {
            private readonly IDictionary<TKey, TValue> _dictionary;
            private readonly IEnumerator<KeyValuePair<TKey, TValue>> _enumerator;

            public DictionaryEnumerator(IDictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _enumerator = _dictionary.GetEnumerator();
            }

            public DictionaryEntry Entry =>
                new DictionaryEntry(_enumerator.Current.Key, _enumerator.Current.Value);

            public object Key => _enumerator.Current.Key;

            public object? Value => _enumerator.Current.Value;

            public object Current => Entry;

            public bool MoveNext() => _enumerator.MoveNext();

            public void Reset() => _enumerator.Reset();
        }

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        public sealed class KeyCollection : ICollection<TKey>, ICollection,
            IReadOnlyCollection<TKey>
        {
            private readonly ICollection<TKey> _collection;

            internal KeyCollection(ICollection<TKey> collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                _collection = collection;
            }

            void ICollection<TKey>.Add(TKey item)
            {
                throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
            }

            void ICollection<TKey>.Clear()
            {
                throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
            }

            public bool Contains(TKey item) => _collection.Contains(item);

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                _collection.CopyTo(array, arrayIndex);
            }

            public int Count => _collection.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            bool ICollection<TKey>.Remove(TKey item)
            {
                throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
            }

            public IEnumerator<TKey> GetEnumerator() => _collection.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_collection).GetEnumerator();

            void ICollection.CopyTo(Array array, int index)
            {
                CollectionHelpers.CopyTo(_collection, array, index);
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => _collection is ICollection collection
                ? collection.SyncRoot
                : this;
        }

        public sealed class ValueCollection : ICollection<TValue>, ICollection,
            IReadOnlyCollection<TValue>
        {
            private readonly ICollection<TValue> _collection;

            internal ValueCollection(ICollection<TValue> collection)
            {
                ArgumentNullException.ThrowIfNull(collection);
                _collection = collection;
            }

            void ICollection<TValue>.Add(TValue item)
            {
                throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
            }

            void ICollection<TValue>.Clear()
            {
                throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
            }

            bool ICollection<TValue>.Contains(TValue item) => _collection.Contains(item);

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                _collection.CopyTo(array, arrayIndex);
            }

            public int Count => _collection.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            bool ICollection<TValue>.Remove(TValue item)
            {
                throw new NotSupportedException(SR.NotSupported_ReadOnlyCollection);
            }

            public IEnumerator<TValue> GetEnumerator() => _collection.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_collection).GetEnumerator();

            void ICollection.CopyTo(Array array, int index)
            {
                CollectionHelpers.CopyTo(_collection, array, index);
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => _collection is ICollection collection
                ? collection.SyncRoot
                : this;
        }
    }
}
