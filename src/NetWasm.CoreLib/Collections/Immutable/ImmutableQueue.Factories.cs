// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Immutable;

/// <summary>Factory methods for immutable queues.</summary>
public static class ImmutableQueue
{
    public static ImmutableQueue<T> Create<T>() => ImmutableQueue<T>.Empty;

    public static ImmutableQueue<T> Create<T>(T item) =>
        ImmutableQueue<T>.Empty.Enqueue(item);

    public static ImmutableQueue<T> CreateRange<T>(IEnumerable<T> items)
    {
        Requires.NotNull(items, nameof(items));
        if (items is T[] array)
        {
            return Create(array);
        }

        using var enumerator = items.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return ImmutableQueue<T>.Empty;
        }

        var forwards = ImmutableStack.Create(enumerator.Current);
        var backwards = ImmutableStack<T>.Empty;
        while (enumerator.MoveNext())
        {
            backwards = backwards.Push(enumerator.Current);
        }

        return new ImmutableQueue<T>(forwards, backwards);
    }

    public static ImmutableQueue<T> Create<T>(params T[] items)
    {
        Requires.NotNull(items, nameof(items));
        return Create((ReadOnlySpan<T>)items);
    }

    public static ImmutableQueue<T> Create<T>(params ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
        {
            return ImmutableQueue<T>.Empty;
        }

        var forwards = ImmutableStack<T>.Empty;
        for (var index = items.Length - 1; index >= 0; index--)
        {
            forwards = forwards.Push(items[index]);
        }

        return new ImmutableQueue<T>(forwards, ImmutableStack<T>.Empty);
    }

    public static IImmutableQueue<T> Dequeue<T>(
        this IImmutableQueue<T> queue,
        out T value)
    {
        Requires.NotNull(queue, nameof(queue));
        value = queue.Peek();
        return queue.Dequeue();
    }
}
