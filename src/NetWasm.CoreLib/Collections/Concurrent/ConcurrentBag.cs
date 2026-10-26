// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime ConcurrentBag at
// 811225a482702af7ecc35d817966bc70b88a3a23 for one managed reactor.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections.Concurrent;

// Upstream uses a local LIFO queue per managed thread plus cross-thread stealing.
// One reactor needs only the local store. Null preserves the upstream distinction
// between no queue ever created and an allocated queue that is currently empty.
[DebuggerDisplay("Count = {Count}")]
public class ConcurrentBag<T> : IProducerConsumerCollection<T>, IReadOnlyCollection<T>
{
    private Stack<T>? _items;

    public ConcurrentBag() { }

    public ConcurrentBag(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = new Stack<T>(collection);
    }

    public void Add(T item) => (_items ??= new Stack<T>()).Push(item);

    public bool TryTake([MaybeNullWhen(false)] out T result)
    {
        if (_items is not null) return _items.TryPop(out result);
        result = default;
        return false;
    }

    public bool TryPeek([MaybeNullWhen(false)] out T result)
    {
        if (_items is not null) return _items.TryPeek(out result);
        result = default;
        return false;
    }

    public int Count => _items?.Count ?? 0;
    public bool IsEmpty => Count == 0;
    public void Clear() => _items?.Clear();
    public T[] ToArray() => _items?.ToArray() ?? Array.Empty<T>();

    public void CopyTo(T[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (_items is null) return;
        if (index > array.Length - _items.Count)
            throw new ArgumentException("The number of elements exceeds the available space in the destination array.", nameof(index));
        try
        {
            foreach (var item in _items) array[index++] = item;
        }
        catch (ArrayTypeMismatchException exception)
        {
            throw new InvalidCastException(exception.Message, exception);
        }
    }

    void ICollection.CopyTo(Array array, int index)
    {
        if (array is T[] typed)
        {
            CopyTo(typed, index);
            return;
        }
        ArgumentNullException.ThrowIfNull(array);
        ToArray().CopyTo(array, index);
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)ToArray()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => throw new NotSupportedException("The SyncRoot property may not be used for synchronization of concurrent collections.");
    bool IProducerConsumerCollection<T>.TryAdd(T item)
    {
        Add(item);
        return true;
    }
}
