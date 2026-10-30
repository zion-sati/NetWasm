// Adapted from dotnet/runtime ConcurrentQueue at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections.Concurrent;

// As with ConcurrentDictionary, managed access runs on a single reactor.
// Queue supplies ring-buffer storage. Enumeration snapshots at GetEnumerator,
// so later mutations cannot invalidate or change an existing enumeration.
[DebuggerDisplay("Count = {Count}")]
public class ConcurrentQueue<T> : IProducerConsumerCollection<T>, IReadOnlyCollection<T>
{
    private readonly Queue<T> _queue;

    public ConcurrentQueue() => _queue = new Queue<T>();

    public ConcurrentQueue(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _queue = new Queue<T>(collection);
    }

    public int Count => _queue.Count;
    public bool IsEmpty => _queue.Count == 0;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => throw new NotSupportedException();

    public void Enqueue(T item) => _queue.Enqueue(item);
    public bool TryDequeue([MaybeNullWhen(false)] out T result) => _queue.TryDequeue(out result);
    public bool TryPeek([MaybeNullWhen(false)] out T result) => _queue.TryPeek(out result);
    public void Clear() => _queue.Clear();
    public T[] ToArray() => _queue.ToArray();

    public void CopyTo(T[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index > array.Length - Count)
        {
            throw new ArgumentException("The destination array has insufficient space.");
        }
        _queue.CopyTo(array, index);
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

    public IEnumerator<T> GetEnumerator() => Enumerate(ToArray());

    private static IEnumerator<T> Enumerate(T[] snapshot)
    {
        foreach (var item in snapshot)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    bool IProducerConsumerCollection<T>.TryTake([MaybeNullWhen(false)] out T item) => TryDequeue(out item);
    bool IProducerConsumerCollection<T>.TryAdd(T item)
    {
        Enqueue(item);
        return true;
    }
}
