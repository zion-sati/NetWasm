// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Collections.Immutable;

/// <summary>An immutable queue.</summary>
/// <typeparam name="T">The element type.</typeparam>
[CollectionBuilder(typeof(ImmutableQueue), nameof(ImmutableQueue.Create))]
public sealed partial class ImmutableQueue<T> : IImmutableQueue<T>
{
    private static readonly ImmutableQueue<T> s_emptyField =
        new(ImmutableStack<T>.Empty, ImmutableStack<T>.Empty);

    private readonly ImmutableStack<T> _backwards;
    private readonly ImmutableStack<T> _forwards;
    private ImmutableStack<T>? _backwardsReversed;

    internal ImmutableQueue(ImmutableStack<T> forwards, ImmutableStack<T> backwards)
    {
        _forwards = forwards;
        _backwards = backwards;
    }

    public ImmutableQueue<T> Clear() => Empty;

    public bool IsEmpty => _forwards.IsEmpty;

    public static ImmutableQueue<T> Empty => s_emptyField;

    IImmutableQueue<T> IImmutableQueue<T>.Clear() => Clear();

    private ImmutableStack<T> BackwardsReversed =>
        _backwardsReversed ??= _backwards.Reverse();

    public T Peek()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException();
        }

        return _forwards.Peek();
    }

    public ref readonly T PeekRef()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException();
        }

        return ref _forwards.PeekRef();
    }

    public ImmutableQueue<T> Enqueue(T value)
    {
        if (IsEmpty)
        {
            return new ImmutableQueue<T>(
                ImmutableStack.Create(value), ImmutableStack<T>.Empty);
        }

        return new ImmutableQueue<T>(_forwards, _backwards.Push(value));
    }

    IImmutableQueue<T> IImmutableQueue<T>.Enqueue(T value) => Enqueue(value);

    public ImmutableQueue<T> Dequeue()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException();
        }

        var forwards = _forwards.Pop();
        if (!forwards.IsEmpty)
        {
            return new ImmutableQueue<T>(forwards, _backwards);
        }

        if (_backwards.IsEmpty)
        {
            return Empty;
        }

        return new ImmutableQueue<T>(BackwardsReversed, ImmutableStack<T>.Empty);
    }

    public ImmutableQueue<T> Dequeue(out T value)
    {
        value = Peek();
        return Dequeue();
    }

    IImmutableQueue<T> IImmutableQueue<T>.Dequeue() => Dequeue();

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new EnumeratorObject(this);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        new EnumeratorObject(this);
}
