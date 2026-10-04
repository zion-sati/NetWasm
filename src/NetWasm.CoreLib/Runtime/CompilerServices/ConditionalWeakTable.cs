// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.CompilerServices;

public sealed class ConditionalWeakTable<TKey, TValue>
    where TKey : class
    where TValue : class
{
    private Entry? _entries;

    public delegate TValue CreateValueCallback(TKey key);

    public ConditionalWeakTable()
    {
    }

    ~ConditionalWeakTable()
    {
        var entry = _entries;
        _entries = null;
        while (entry != null)
        {
            ConditionalWeakTableRuntime.Release(entry.Handle);
            entry = entry.Next;
        }
    }

    public void Add(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (TryGetValue(key, out _))
        {
            throw new ArgumentException();
        }

        _entries = new Entry(
            ConditionalWeakTableRuntime.Create(key, value),
            _entries);
    }

    public TValue GetOrCreateValue(TKey key) =>
        GetValue(key, static _ => Activator.CreateInstance<TValue>());

    public TValue GetValue(TKey key, CreateValueCallback createValueCallback)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(createValueCallback);
        if (TryGetValue(key, out var existing))
        {
            return existing;
        }

        var created = createValueCallback(key);
        if (TryGetValue(key, out existing))
        {
            return existing;
        }

        _entries = new Entry(
            ConditionalWeakTableRuntime.Create(key, created),
            _entries);
        return created;
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Entry? previous = null;
        var entry = _entries;
        while (entry != null)
        {
            var next = entry.Next;
            var storedKey = ConditionalWeakTableRuntime.GetKey(entry.Handle);
            if (storedKey == null || ReferenceEquals(storedKey, key))
            {
                Unlink(previous, entry, next);
                if (storedKey != null)
                {
                    return true;
                }
            }
            else
            {
                previous = entry;
            }
            entry = next;
        }
        return false;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        Entry? previous = null;
        var entry = _entries;
        while (entry != null)
        {
            var next = entry.Next;
            var storedKey = ConditionalWeakTableRuntime.GetKey(entry.Handle);
            if (storedKey == null)
            {
                Unlink(previous, entry, next);
            }
            else
            {
                if (ReferenceEquals(storedKey, key))
                {
                    value = (TValue)ConditionalWeakTableRuntime.GetValue(entry.Handle)!;
                    return true;
                }
                previous = entry;
            }
            entry = next;
        }

        value = default!;
        return false;
    }

    private void Unlink(Entry? previous, Entry entry, Entry? next)
    {
        if (previous == null)
        {
            _entries = next;
        }
        else
        {
            previous.Next = next;
        }
        ConditionalWeakTableRuntime.Release(entry.Handle);
    }

    private sealed class Entry(int handle, Entry? next)
    {
        internal int Handle { get; } = handle;

        internal Entry? Next { get; set; } = next;
    }
}
