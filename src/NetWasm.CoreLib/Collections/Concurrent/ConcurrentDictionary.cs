// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace System.Collections.Concurrent;

// Managed code runs on a single reactor, so Dictionary provides the storage
// without locks. Retry loops around user factories still preserve the public
// ConcurrentDictionary contract when a factory reenters and changes this map.
public class ConcurrentDictionary<TKey, TValue> :
    IDictionary<TKey, TValue>,
    IReadOnlyDictionary<TKey, TValue>,
    IDictionary
{
    private readonly Dictionary<TKey, TValue> _dictionary;

    public ConcurrentDictionary() : this(EqualityComparer<TKey>.Default)
    {
    }

    public ConcurrentDictionary(IEqualityComparer<TKey> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _dictionary = new Dictionary<TKey, TValue>(comparer);
    }

    public ConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) :
        this(collection, EqualityComparer<TKey>.Default)
    {
    }

    public ConcurrentDictionary(
        IEnumerable<KeyValuePair<TKey, TValue>> collection,
        IEqualityComparer<TKey> comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(comparer);
        _dictionary = new Dictionary<TKey, TValue>(collection, comparer);
    }

    public ConcurrentDictionary(int concurrencyLevel, int capacity) :
        this(concurrencyLevel, capacity, EqualityComparer<TKey>.Default)
    {
    }

    public ConcurrentDictionary(
        int concurrencyLevel,
        int capacity,
        IEqualityComparer<TKey> comparer)
    {
        ValidateConcurrencyLevel(concurrencyLevel);
        ArgumentNullException.ThrowIfNull(comparer);
        _dictionary = new Dictionary<TKey, TValue>(capacity, comparer);
    }

    public ConcurrentDictionary(
        int concurrencyLevel,
        IEnumerable<KeyValuePair<TKey, TValue>> collection,
        IEqualityComparer<TKey> comparer)
    {
        ValidateConcurrencyLevel(concurrencyLevel);
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(comparer);
        _dictionary = new Dictionary<TKey, TValue>(collection, comparer);
    }

    public int Count => _dictionary.Count;

    public bool IsEmpty => _dictionary.Count == 0;

    public TValue this[TKey key]
    {
        get => _dictionary[key];
        set => _dictionary[key] = value;
    }

    public ICollection<TKey> Keys => SnapshotKeys();

    public ICollection<TValue> Values => SnapshotValues();

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    bool IDictionary.IsFixedSize => false;

    bool IDictionary.IsReadOnly => false;

    ICollection IDictionary.Keys => (ICollection)Keys;

    ICollection IDictionary.Values => (ICollection)Values;

    object? IDictionary.this[object key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return key is TKey typedKey && TryGetValue(typedKey, out var value)
                ? value
                : null;
        }
        set => this[CastKey(key)] = CastValue(value);
    }

    public TValue AddOrUpdate(
        TKey key,
        TValue addValue,
        Func<TKey, TValue, TValue> updateValueFactory)
    {
        ArgumentNullException.ThrowIfNull(updateValueFactory);
        while (true)
        {
            if (TryGetValue(key, out var oldValue))
            {
                var newValue = updateValueFactory(key, oldValue);
                if (TryUpdate(key, newValue, oldValue))
                {
                    return newValue;
                }
            }
            else if (TryAdd(key, addValue))
            {
                return addValue;
            }
        }
    }

    public TValue AddOrUpdate(
        TKey key,
        Func<TKey, TValue> addValueFactory,
        Func<TKey, TValue, TValue> updateValueFactory)
    {
        ArgumentNullException.ThrowIfNull(addValueFactory);
        ArgumentNullException.ThrowIfNull(updateValueFactory);
        while (true)
        {
            if (TryGetValue(key, out var oldValue))
            {
                var newValue = updateValueFactory(key, oldValue);
                if (TryUpdate(key, newValue, oldValue))
                {
                    return newValue;
                }
            }
            else
            {
                var addValue = addValueFactory(key);
                if (TryAdd(key, addValue))
                {
                    return addValue;
                }
            }
        }
    }

    public void Clear() => _dictionary.Clear();

    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<TKey, TValue>>)ToArray()).GetEnumerator();

    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (TryGetValue(key, out var existing))
        {
            return existing;
        }
        return TryAdd(key, value) ? value : _dictionary[key];
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        if (TryGetValue(key, out var existing))
        {
            return existing;
        }

        var value = valueFactory(key);
        return TryAdd(key, value) ? value : _dictionary[key];
    }

    public KeyValuePair<TKey, TValue>[] ToArray()
    {
        var result = new KeyValuePair<TKey, TValue>[_dictionary.Count];
        _dictionary.CopyTo(result, 0);
        return result;
    }

    public bool TryAdd(TKey key, TValue value) => _dictionary.TryAdd(key, value);

    public bool TryGetValue(TKey key, out TValue value) =>
        _dictionary.TryGetValue(key, out value);

    public bool TryRemove(TKey key, out TValue value) =>
        _dictionary.Remove(key, out value);

    public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
    {
        if (!_dictionary.TryGetValue(key, out var currentValue) ||
            !EqualityComparer<TValue>.Default.Equals(currentValue, comparisonValue))
        {
            return false;
        }

        _dictionary[key] = newValue;
        return true;
    }

    void ICollection<KeyValuePair<TKey, TValue>>.Add(
        KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(
        KeyValuePair<TKey, TValue> item) =>
        TryGetValue(item.Key, out var value) &&
        EqualityComparer<TValue>.Default.Equals(value, item.Value);

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(
        KeyValuePair<TKey, TValue>[] array,
        int arrayIndex) => _dictionary.CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(
        KeyValuePair<TKey, TValue> item)
    {
        if (!((ICollection<KeyValuePair<TKey, TValue>>)this).Contains(item))
        {
            return false;
        }
        return TryRemove(item.Key, out _);
    }

    void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => Add(key, value);

    bool IDictionary<TKey, TValue>.Remove(TKey key) => TryRemove(key, out _);

    IEnumerator<KeyValuePair<TKey, TValue>>
        IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ValidateCopyIndex(array, index);

        if (array is KeyValuePair<TKey, TValue>[] pairs)
        {
            _dictionary.CopyTo(pairs, index);
            return;
        }

        if (array is DictionaryEntry[] entries)
        {
            foreach (var pair in ToArray())
            {
                entries[index++] = new DictionaryEntry(pair.Key!, pair.Value);
            }
            return;
        }

        if (array is object?[] objects)
        {
            foreach (var pair in ToArray())
            {
                objects[index++] = pair;
            }
            return;
        }

        throw new ArgumentException();
    }

    void IDictionary.Add(object key, object? value) =>
        Add(CastKey(key), CastValue(value));

    bool IDictionary.Contains(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is TKey typedKey && ContainsKey(typedKey);
    }

    IDictionaryEnumerator IDictionary.GetEnumerator() =>
        new DictionaryEnumerator(ToArray());

    void IDictionary.Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key is TKey typedKey)
        {
            TryRemove(typedKey, out _);
        }
    }

    private void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException();
        }
    }

    private ReadOnlyCollection<TKey> SnapshotKeys()
    {
        var keys = new List<TKey>(_dictionary.Count);
        foreach (var pair in _dictionary)
        {
            keys.Add(pair.Key);
        }
        return new ReadOnlyCollection<TKey>(keys);
    }

    private ReadOnlyCollection<TValue> SnapshotValues()
    {
        var values = new List<TValue>(_dictionary.Count);
        foreach (var pair in _dictionary)
        {
            values.Add(pair.Value);
        }
        return new ReadOnlyCollection<TValue>(values);
    }

    private static TKey CastKey(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is TKey typedKey ? typedKey : throw new ArgumentException();
    }

    private static TValue CastValue(object? value)
    {
        if (value is TValue typedValue)
        {
            return typedValue;
        }
        if (value == null && default(TValue) is null)
        {
            return default!;
        }
        throw new ArgumentException();
    }

    private static void ValidateConcurrencyLevel(int concurrencyLevel)
    {
        if (concurrencyLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrencyLevel));
        }
    }

    private void ValidateCopyIndex(Array array, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (index > array.Length || array.Length - index < Count)
        {
            throw new ArgumentException();
        }
    }

    private sealed class DictionaryEnumerator(
        KeyValuePair<TKey, TValue>[] pairs) : IDictionaryEnumerator
    {
        private int _index = -1;

        public DictionaryEntry Entry => new(CurrentPair.Key!, CurrentPair.Value);

        public object Key => CurrentPair.Key!;

        public object? Value => CurrentPair.Value;

        public object Current => Entry;

        public bool MoveNext()
        {
            if (_index + 1 >= pairs.Length)
            {
                _index = pairs.Length;
                return false;
            }
            _index++;
            return true;
        }

        public void Reset() => _index = -1;

        private KeyValuePair<TKey, TValue> CurrentPair =>
            _index >= 0 && _index < pairs.Length
                ? pairs[_index]
                : throw new InvalidOperationException();
    }
}
