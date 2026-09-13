// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Immutable;

/// <summary>An immutable stack.</summary>
/// <typeparam name="T">The element type.</typeparam>
[CollectionBuilder(typeof(ImmutableStack), nameof(ImmutableStack.Create))]
public sealed partial class ImmutableStack<T> : IImmutableStack<T>
{
    private static readonly ImmutableStack<T> s_emptyField = new();

    private readonly T? _head;
    private readonly ImmutableStack<T>? _tail;

    private ImmutableStack()
    {
    }

    private ImmutableStack(T head, ImmutableStack<T> tail)
    {
        _head = head;
        _tail = tail;
    }

    public static ImmutableStack<T> Empty => s_emptyField;

    public ImmutableStack<T> Clear() => Empty;

    IImmutableStack<T> IImmutableStack<T>.Clear() => Clear();

    public bool IsEmpty => _tail is null;

    public T Peek()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException();
        }

        return _head!;
    }

    public ref readonly T PeekRef()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException();
        }

        return ref _head!;
    }

    public ImmutableStack<T> Push(T value) => new(value, this);

    IImmutableStack<T> IImmutableStack<T>.Push(T value) => Push(value);

    public ImmutableStack<T> Pop()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException();
        }

        return _tail!;
    }

    public ImmutableStack<T> Pop(out T value)
    {
        value = Peek();
        return Pop();
    }

    IImmutableStack<T> IImmutableStack<T>.Pop() => Pop();

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new EnumeratorObject(this);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        new EnumeratorObject(this);

    internal ImmutableStack<T> Reverse()
    {
        var result = Clear();
        for (var current = this; !current.IsEmpty; current = current.Pop())
        {
            result = result.Push(current.Peek());
        }

        return result;
    }
}
